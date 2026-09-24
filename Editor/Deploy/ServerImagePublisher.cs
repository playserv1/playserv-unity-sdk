using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Playserv.Editor
{
    internal interface IServerImageProcess
    {
        Task<ServerImageProcessResult> RunAsync(string[] args, string directory, IReadOnlyDictionary<string, string> environment, string input, TimeSpan timeout, CancellationToken ct);
    }
    internal sealed class ServerImageProcessResult
    {
        internal int ExitCode;
        internal string Output;
    }
    internal sealed class BuiltServerImage
    {
        internal string Id { get; }
        internal string Context { get; }
        internal string Dockerfile { get; }
        internal BuiltServerImage(string id, string context, string dockerfile) { Id = id; Context = context; Dockerfile = dockerfile; }
    }
    internal sealed class ServerImagePublication
    {
        public string Target, Registry, Repository, Server, Tag, ImageId, Digest;
    }
    internal sealed class ServerImagePublisher
    {
        private readonly PlatformFunctionClient _api;
        private readonly IServerImageProcess _process;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly TimeSpan _verificationBudget;
        internal ServerImagePublication LastPublication { get; private set; }
        internal ServerImagePublisher(PlatformFunctionClient api, IServerImageProcess process, Func<TimeSpan, CancellationToken, Task> delay = null, TimeSpan? verificationBudget = null)
        {
            _api = api; _process = process; _delay = delay ?? Task.Delay;
            _verificationBudget = verificationBudget ?? TimeSpan.FromSeconds(60);
            if (_verificationBudget <= TimeSpan.Zero || _verificationBudget > TimeSpan.FromSeconds(60)) throw new ArgumentOutOfRangeException(nameof(verificationBudget));
        }

        internal async Task CheckDockerAsync(CancellationToken ct)
        {
            var result = await Docker(new[] { "info", "--format", "{{.OSType}}" }, null, null, null, TimeSpan.FromSeconds(30), ct);
            if (result.Output.Trim() != "linux") throw new InvalidOperationException("Docker must use a Linux daemon. Switch Docker Desktop to Linux containers.");
        }

        internal async Task<BuiltServerImage> BuildAsync(string context, string dockerfile, CancellationToken ct)
        {
            var folder = Path.GetFullPath(context ?? "");
            var file = Path.GetFullPath(Path.Combine(folder, dockerfile ?? ""));
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!Directory.Exists(folder) || !File.Exists(file) || !file.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
                throw new InvalidOperationException("Choose an existing Dockerfile inside the build context folder.");
            for (var path = file; !string.Equals(path, folder, comparison); path = Path.GetDirectoryName(path))
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("The Dockerfile path must not contain symbolic links.");
            await CheckDockerAsync(ct);
            var iidFile = Path.Combine(Path.GetTempPath(), "playserv-image-" + Guid.NewGuid().ToString("N"));
            try
            {
                await Docker(new[] { "build", "--platform", "linux/amd64", "--load", "--provenance=false", "--iidfile", iidFile, "--file", file, folder }, folder, null, null, TimeSpan.FromMinutes(30), ct);
                if (!File.Exists(iidFile)) throw new InvalidOperationException("Docker did not return a built image ID.");
                var id = File.ReadAllText(iidFile).Trim();
                ValidateDigest(id);
                await VerifyLocalImage(id, ct);
                return new BuiltServerImage(id, folder, file);
            }
            finally { if (File.Exists(iidFile)) File.Delete(iidFile); }
        }

        private async Task VerifyLocalImage(string id, CancellationToken ct)
        {
            var result = await Docker(new[] { "image", "inspect", id }, null, null, null, TimeSpan.FromSeconds(30), ct);
            var rows = JArray.Parse(result.Output);
            if (rows.Count != 1 || (string)rows[0]["Id"] != id || (string)rows[0]["Os"] != "linux" || (string)rows[0]["Architecture"] != "amd64")
                throw new InvalidOperationException("The built image must be linux/amd64. Check Docker's build platform and emulation support.");
        }

        internal async Task PublishAsync(BuiltServerImage image, string server, string tag, CancellationToken ct)
        {
            if (image == null) throw new InvalidOperationException("Build and review an image before publishing.");
            if (!Regex.IsMatch(server ?? "", "^[a-z0-9]+(?:[._-][a-z0-9]+)*$")) throw new InvalidOperationException("Select a game server with a valid image name.");
            if (!Regex.IsMatch(tag ?? "", "^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$")) throw new InvalidOperationException("Enter a Docker tag of up to 128 letters, digits, underscores, periods or hyphens.");
            if (LastPublication != null && LastPublication.Target == _api.ImageTarget && LastPublication.Server == server && LastPublication.Tag == tag)
                throw new InvalidOperationException("This publication was already attempted. Check publication or choose a new tag.");
            await VerifyLocalImage(image.Id, ct);
            await _api.RefreshImageSessionAsync(ct);
            if (!Array.Exists(await _api.ListGameServersAsync(ct), slug => slug == server)) throw new InvalidOperationException("The selected game server is no longer available in this project/environment.");
            var listing = await _api.ListServerImagesAsync(ct);
            if (FindTag(listing, server, tag) != null) throw new InvalidOperationException("This tag already exists in the project's registry. Choose a new tag.");
            var credentials = await _api.IssueImageCredentialsAsync(ct);
            var registry = (string)credentials["registry"];
            var repository = (string)credentials["repository"];
            ValidateRegistry(registry, repository);
            if ((string)listing["registry"] != registry || (string)listing["repository"] != repository)
                throw new InvalidOperationException("The registry target changed. Reconnect and review the project.");
            if ((string)credentials["username"] != "oauth2accesstoken" || string.IsNullOrEmpty((string)credentials["secret"]) ||
                !DateTimeOffset.TryParse((string)credentials["expires_at"], out var expires) || expires <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("The registry credential is invalid or expired.");
            var reference = registry + "/" + repository + "/" + server + ":" + tag;
            var config = Path.Combine(Path.GetTempPath(), "playserv-docker-" + Guid.NewGuid().ToString("N"));
            try
            {
                ServerImageProcess.CreatePrivateDirectory(config);
                // Prevent Docker Desktop from selecting an OS credential helper for this temporary login.
                File.WriteAllText(Path.Combine(config, "config.json"), "{\"auths\":{\"" + registry + "\":{}}}");
                var host = Environment.GetEnvironmentVariable("DOCKER_HOST");
                if (string.IsNullOrWhiteSpace(host) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_CONTEXT")))
                    host = (await Docker(new[] { "context", "inspect", "--format", "{{.Endpoints.docker.Host}}" }, null, null, null, TimeSpan.FromSeconds(30), ct)).Output.Trim();
                if (!host.StartsWith("unix://", StringComparison.Ordinal) && !host.StartsWith("npipe://", StringComparison.Ordinal))
                    throw new InvalidOperationException("Publishing requires a local Docker Linux daemon (Unix socket or Windows named pipe). Select a local Docker context.");
                var env = new Dictionary<string, string> { ["DOCKER_CONFIG"] = config, ["DOCKER_HOST"] = host, ["DOCKER_CONTEXT"] = null };
                await Docker(new[] { "login", "--username", "oauth2accesstoken", "--password-stdin", registry }, null, env, (string)credentials["secret"] + "\n", TimeSpan.FromSeconds(30), ct);
                await Docker(new[] { "tag", image.Id, reference }, null, env, null, TimeSpan.FromSeconds(30), ct);
                ct.ThrowIfCancellationRequested();
                LastPublication = new ServerImagePublication { Target = _api.ImageTarget, Registry = registry, Repository = repository, Server = server, Tag = tag, ImageId = image.Id };
                try
                {
                    var result = await Docker(new[] { "push", reference }, null, env, null, TimeSpan.FromMinutes(30), ct);
                    var match = Regex.Match(result.Output, @"digest:\s*(sha256:[a-f0-9]{64})\b");
                    if (match.Success) LastPublication.Digest = match.Groups[1].Value;
                    else throw new InvalidOperationException("Docker did not return a manifest digest.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { throw new InvalidOperationException("Push result is uncertain. Use Check publication before any further action. " + _api.Redact(error.Message)); }
            }
            finally { if (Directory.Exists(config)) Directory.Delete(config, true); }
            ct.ThrowIfCancellationRequested();
            await CheckPublicationAsync(LastPublication, ct);
        }

        internal async Task<bool> CheckPublicationAsync(ServerImagePublication publication, CancellationToken ct)
        {
            if (publication == null || publication.Target != _api.ImageTarget) throw new InvalidOperationException("Reconnect to the original API, project and environment to check this publication.");
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(_verificationBudget);
                try
                {
                    while (true)
                    {
                        var wait = TimeSpan.FromSeconds(2);
                        try
                        {
                            var listing = await _api.ListServerImagesAsync(deadline.Token);
                            if ((string)listing["registry"] != publication.Registry || (string)listing["repository"] != publication.Repository)
                                throw new InvalidOperationException("Publication belongs to a different registry repository.");
                            var remote = FindTag(listing, publication.Server, publication.Tag);
                            if (remote != null)
                            {
                                var architecture = (string)remote["architecture"];
                                if (architecture != "amd64" && architecture != "multi") throw new InvalidOperationException("Published image is not compatible with amd64 pools (" + architecture + ").");
                                if (string.IsNullOrEmpty(publication.Digest)) return false;
                                if ((string)remote["digest"] != publication.Digest) throw new InvalidOperationException("The registry tag points to a different manifest digest. Publication was not verified.");
                                return true;
                            }
                        }
                        catch (ServerImageRequestException error) when (error.Status == 429 || error.Status == 503)
                        { wait = error.RetryAfter ?? wait; }
                        await _delay(wait, deadline.Token);
                        deadline.Token.ThrowIfCancellationRequested();
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { throw new TimeoutException("Publication was not verified within 60 seconds. Use Check publication; do not repeat the push."); }
            }
        }

        private async Task<ServerImageProcessResult> Docker(string[] args, string directory, IReadOnlyDictionary<string, string> environment, string input, TimeSpan timeout, CancellationToken ct)
        {
            ServerImageProcessResult result;
            try { result = await _process.RunAsync(args, directory, environment, input, timeout, ct); }
            catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("Docker CLI is unavailable. Install Docker and make it available on the Editor's PATH."); }
            ct.ThrowIfCancellationRequested();
            if (result.ExitCode != 0) throw new InvalidOperationException("Docker " + args[0] + " failed. " + _api.Redact(result.Output));
            return result;
        }
        private static JToken FindTag(JObject listing, string server, string tag)
        {
            if (!(listing["images"] is JArray images)) throw new InvalidOperationException("Invalid server-image listing response.");
            foreach (var image in images)
            {
                if ((string)image["name"] != server) continue;
                if (!(image["tags"] is JArray tags)) throw new InvalidOperationException("Invalid image tags response.");
                foreach (var item in tags) if ((string)item["tag"] == tag) return item;
            }
            return null;
        }
        private static void ValidateDigest(string id)
        { if (!Regex.IsMatch(id ?? "", "^sha256:[a-f0-9]{64}$")) throw new InvalidOperationException("Docker returned an invalid image ID."); }
        private static void ValidateRegistry(string registry, string repository)
        {
            if (!Regex.IsMatch(registry ?? "", "^[a-zA-Z0-9][a-zA-Z0-9.-]*(?::[0-9]{1,5})?$") ||
                !Regex.IsMatch(repository ?? "", "^[a-z0-9]+(?:[._-][a-z0-9]+)*(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)*$"))
                throw new InvalidOperationException("The API returned an invalid registry destination.");
        }
    }
}

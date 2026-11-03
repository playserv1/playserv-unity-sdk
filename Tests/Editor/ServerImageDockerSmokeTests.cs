using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Playserv.Editor.Tests
{
    public class ServerImageDockerSmokeTests
    {
        [Test, Explicit("Requires Docker, a loopback registry and unity/tools/server-image-smoke/serve.py. See its README.")]
        public void LocalRegistryVerifiesTheImageBuiltByTheEditorPublisher()
        {
            var origin = Environment.GetEnvironmentVariable("PLAYSERV_IMAGE_SMOKE_API");
            if (string.IsNullOrWhiteSpace(origin)) Assert.Ignore("Set PLAYSERV_IMAGE_SMOKE_API to the loopback fixture to run the Docker smoke.");
            Assert.That(new Uri(origin).IsLoopback, Is.True);
            Task.Run(async () =>
            {
                var folder = Path.Combine(Path.GetTempPath(), "psv smoke " + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "Dockerfile"), "FROM scratch\nCOPY payload /payload\nENTRYPOINT [\"/payload\"]\n");
                File.WriteAllText(Path.Combine(folder, "payload"), "local build fixture\n");
                try
                {
                    using (var api = new PlatformFunctionClient(origin, "sk_local_fixture"))
                    {
                        await api.ConnectAsync(default);
                        var publisher = new ServerImagePublisher(api, new ServerImageProcess(api.Redact));
                        var image = await publisher.BuildAsync(folder, "Dockerfile", default);
                        await publisher.PublishAsync(image, "smoke-room", "test-" + Guid.NewGuid().ToString("N"), default);
                        Assert.That(publisher.LastPublication.Digest, Does.StartWith("sha256:"));
                        Assert.That(await publisher.CheckPublicationAsync(publisher.LastPublication, default), Is.True);
                    }
                }
                finally { Directory.Delete(folder, true); }
            }).GetAwaiter().GetResult();
        }
    }
}

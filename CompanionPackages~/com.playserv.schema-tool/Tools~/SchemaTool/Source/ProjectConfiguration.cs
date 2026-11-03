using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlayServ.Schema.Tool;

internal static class ProjectConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string GetPath(string projectRoot)
    {
        return Path.Combine(projectRoot, ToolConstants.ConfigurationFileName);
    }

    public static SchemaToolConfiguration Load(string projectRoot)
    {
        var path = GetPath(projectRoot);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Schema configuration was not found. Run 'playserv-schema init' first.",
                path);
        }

        var configuration = JsonSerializer.Deserialize<SchemaToolConfiguration>(
            File.ReadAllText(path),
            JsonOptions) ?? throw new InvalidDataException($"Invalid schema configuration: {path}");

        Validate(configuration, path);
        return configuration;
    }

    public static bool Initialize(string projectRoot)
    {
        var path = GetPath(projectRoot);
        if (File.Exists(path))
            return false;

        var projectId = Path.GetFileName(
            projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var configuration = new SchemaToolConfiguration
        {
            ProjectId = string.IsNullOrWhiteSpace(projectId)
                ? "playserv-project"
                : projectId,
            Sources =
            {
                new SchemaSourceConfiguration
                {
                    Id = "unity-client",
                    Kind = "csharp",
                    Authority = "contract",
                    Paths = { "Assets/**/*.cs" }
                }
            },
            Targets =
            {
                new SchemaTargetConfiguration
                {
                    Kind = "json-schema",
                    Output = "Assets/PlayServ/Generated/Schemas/playserv.schema.json"
                }
            }
        };

        WriteAtomic(path, JsonSerializer.Serialize(configuration, JsonOptions) + Environment.NewLine);
        EnsureLocalStateIgnore(projectRoot);
        return true;
    }

    public static IReadOnlyList<string> EnumerateSourceFiles(
        string projectRoot,
        SchemaToolConfiguration configuration)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludedOutputs = configuration.Targets
            .Select(target => NormalizeRelative(target.Output))
            .Where(path => path.Length > 0)
            .ToArray();

        foreach (var source in configuration.Sources
                     .Where(source => string.Equals(
                         source.Kind,
                         "csharp",
                         StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var patternValue in source.Paths)
            {
                var pattern = NormalizeRelative(patternValue);
                if (pattern.Length == 0)
                    continue;

                var matcher = new Regex(
                    "^" + GlobToRegex(pattern) + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var searchRoot = ResolveSearchRoot(projectRoot, pattern);
                if (!Directory.Exists(searchRoot))
                    continue;

                foreach (var file in Directory.EnumerateFiles(
                             searchRoot,
                             "*.cs",
                             SearchOption.AllDirectories))
                {
                    var relative = NormalizeRelative(
                        Path.GetRelativePath(projectRoot, file));
                    if (IsExcluded(relative, excludedOutputs) ||
                        !matcher.IsMatch(relative))
                    {
                        continue;
                    }

                    files.Add(Path.GetFullPath(file));
                }
            }
        }

        return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> ResolveWatchRoots(
        string projectRoot,
        SchemaToolConfiguration configuration)
    {
        return configuration.Sources
            .Where(source => string.Equals(
                source.Kind,
                "csharp",
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(source => source.Paths)
            .Select(pattern => ResolveSearchRoot(projectRoot, NormalizeRelative(pattern)))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string ResolveSourceId(
        string projectRoot,
        SchemaToolConfiguration configuration,
        string absoluteFilePath)
    {
        var relative = NormalizeRelative(Path.GetRelativePath(projectRoot, absoluteFilePath));
        foreach (var source in configuration.Sources)
        {
            if (!string.Equals(source.Kind, "csharp", StringComparison.OrdinalIgnoreCase))
                continue;

            if (source.Paths.Any(pattern =>
                    Regex.IsMatch(
                        relative,
                        "^" + GlobToRegex(NormalizeRelative(pattern)) + "$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            {
                return source.Id;
            }
        }

        return "csharp";
    }

    public static string ResolveAuthority(
        SchemaToolConfiguration configuration,
        string sourceId)
    {
        return configuration.Sources.FirstOrDefault(source =>
                   string.Equals(source.Id, sourceId, StringComparison.OrdinalIgnoreCase))
               ?.Authority?.Trim().ToLowerInvariant() ?? "contract";
    }

    public static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions(JsonOptions);
    }

    public static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }

    private static void Validate(
        SchemaToolConfiguration configuration,
        string path)
    {
        if (configuration.SchemaVersion != ToolConstants.ConfigurationVersion)
        {
            throw new InvalidDataException(
                $"Unsupported schemaVersion {configuration.SchemaVersion} in {path}. " +
                $"Expected {ToolConstants.ConfigurationVersion}.");
        }

        if (string.IsNullOrWhiteSpace(configuration.ProjectId))
            throw new InvalidDataException($"projectId is required in {path}.");
        if (configuration.Sources.Count == 0)
            throw new InvalidDataException($"At least one source is required in {path}.");
        if (configuration.Targets.Count == 0)
            throw new InvalidDataException($"At least one target is required in {path}.");
    }

    private static string ResolveSearchRoot(string projectRoot, string pattern)
    {
        var wildcardIndex = pattern.IndexOfAny(new[] { '*', '?' });
        var prefix = wildcardIndex < 0 ? pattern : pattern[..wildcardIndex];
        var separatorIndex = prefix.LastIndexOf('/');
        var relativeRoot = separatorIndex < 0 ? string.Empty : prefix[..separatorIndex];
        return Path.GetFullPath(Path.Combine(
            projectRoot,
            relativeRoot.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool IsExcluded(
        string relativePath,
        IReadOnlyList<string> outputPaths)
    {
        var segments = relativePath.Split('/');
        if (segments.Any(segment =>
                string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "Library", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "Temp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var outputPath in outputPaths)
        {
            if (relativePath.Equals(outputPath, StringComparison.OrdinalIgnoreCase))
                return true;

            var outputDirectory = Path.GetDirectoryName(outputPath)
                ?.Replace('\\', '/')
                .Trim('/');
            if (!string.IsNullOrEmpty(outputDirectory) &&
                relativePath.StartsWith(outputDirectory + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GlobToRegex(string pattern)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '*')
            {
                var isDouble = index + 1 < pattern.Length && pattern[index + 1] == '*';
                if (isDouble)
                {
                    index++;
                    var consumesSlash = index + 1 < pattern.Length && pattern[index + 1] == '/';
                    if (consumesSlash)
                    {
                        index++;
                        builder.Append("(?:.*/)?");
                    }
                    else
                    {
                        builder.Append(".*");
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }

                continue;
            }

            if (character == '?')
            {
                builder.Append("[^/]");
                continue;
            }

            builder.Append(Regex.Escape(character.ToString()));
        }

        return builder.ToString();
    }

    private static string NormalizeRelative(string path)
    {
        return (path ?? string.Empty)
            .Replace('\\', '/')
            .Trim()
            .TrimStart('.', '/');
    }

    private static void EnsureLocalStateIgnore(string projectRoot)
    {
        var stateDirectory = Path.Combine(projectRoot, ".playserv");
        Directory.CreateDirectory(stateDirectory);
        var ignorePath = Path.Combine(stateDirectory, ".gitignore");
        if (File.Exists(ignorePath))
            return;

        File.WriteAllText(
            ignorePath,
            "bin/\n*.lock\n*.pid\nwatch-state.json\n",
            new UTF8Encoding(false));
    }
}

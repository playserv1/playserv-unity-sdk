using System.Text.Json;

namespace PlayServ.Schema.Tool;

internal sealed class ToolApplication
{
    private readonly CliOptions _options;
    private readonly JsonSerializerOptions _jsonOptions =
        ProjectConfiguration.CreateJsonOptions();

    public ToolApplication(CliOptions options)
    {
        _options = options;
    }

    public int Run()
    {
        if (_options.Help)
        {
            WriteHelp();
            return 0;
        }

        return _options.Command switch
        {
            "version" => WriteVersion(),
            "init" => Initialize(),
            "status" => Analyze("status", writeHumanDiagnostics: false),
            "analyze" => Analyze("analyze", writeHumanDiagnostics: true),
            "generate" => Generate("generate", _options.Check),
            "validate" => Generate("validate", checkOnly: true),
            "sync" => Generate("sync", _options.Check),
            "push" => Push(),
            "watch" => Watch(),
            "doctor" => Doctor(),
            _ => throw new ArgumentException($"Unknown command: {_options.Command}")
        };
    }

    private int Initialize()
    {
        Directory.CreateDirectory(_options.ProjectRoot);
        var changed = ProjectConfiguration.Initialize(_options.ProjectRoot);
        var result = BaseResult("init");
        result.Success = true;
        result.GeneratedFilesChanged = changed;
        result.Message = changed
            ? $"Created {ToolConstants.ConfigurationFileName}."
            : $"{ToolConstants.ConfigurationFileName} already exists.";
        WriteResult(result);
        return 0;
    }

    private int Analyze(string command, bool writeHumanDiagnostics)
    {
        var configuration = ProjectConfiguration.Load(_options.ProjectRoot);
        var analysis = CSharpSchemaAnalyzer.Analyze(_options.ProjectRoot, configuration);
        var result = ResultFromAnalysis(command, analysis);
        result.Success = !HasErrors(analysis.Diagnostics);
        result.Message =
            $"Analyzed {analysis.SourceFiles.Count} C# files and found " +
            $"{analysis.Contracts.Count} PlayServ schemas.";
        WriteResult(result, writeHumanDiagnostics);
        return result.Success ? 0 : 1;
    }

    private int Generate(string command, bool checkOnly)
    {
        var configuration = ProjectConfiguration.Load(_options.ProjectRoot);
        var generation = SchemaGenerator.Build(_options.ProjectRoot, configuration);
        if (HasErrors(generation.Analysis.Diagnostics))
        {
            var failedResult = ResultFromAnalysis(command, generation.Analysis);
            failedResult.Success = false;
            failedResult.Message =
                "Schema analysis failed. Generated files were not changed.";
            WriteResult(failedResult, writeHumanDiagnostics: true);
            return 1;
        }

        var hasChanges = SchemaGenerator.Apply(
            _options.ProjectRoot,
            configuration,
            generation,
            checkOnly,
            out var changedPaths);
        var result = ResultFromAnalysis(command, generation.Analysis);
        result.Outputs = generation.Artifacts.Select(artifact => artifact.Path).ToList();
        result.GeneratedFilesChanged = hasChanges;
        result.Success =
            !HasErrors(generation.Analysis.Diagnostics) &&
            (!checkOnly || !hasChanges);
        result.Message = checkOnly
            ? hasChanges
                ? "Generated schema files are out of date: " +
                  string.Join(", ", changedPaths)
                : "Generated schema files are up to date."
            : hasChanges
                ? "Generated: " + string.Join(", ", changedPaths)
                : "Generated schema files are already up to date.";
        WriteResult(result, writeHumanDiagnostics: true);
        return result.Success ? 0 : 1;
    }

    private int Watch()
    {
        var configuration = ProjectConfiguration.Load(_options.ProjectRoot);
        using var watcher = new FileSystemWatchService(
            _options.ProjectRoot,
            configuration,
            () => Generate("watch", checkOnly: false));
        var initialExitCode = Generate("watch", checkOnly: false);
        if (initialExitCode != 0)
            return initialExitCode;

        if (!_options.Json)
            Console.WriteLine("Watching PlayServ schema sources. Press Ctrl+C to stop.");

        watcher.Run();
        return 0;
    }

    private int Push()
    {
        var configuration = ProjectConfiguration.Load(_options.ProjectRoot);
        var generation = SchemaGenerator.Build(_options.ProjectRoot, configuration);
        if (HasErrors(generation.Analysis.Diagnostics))
        {
            var failedResult = ResultFromAnalysis("push", generation.Analysis);
            failedResult.Success = false;
            failedResult.Message = "Schema analysis failed. Nothing was pushed.";
            WriteResult(failedResult, writeHumanDiagnostics: true);
            return 1;
        }

        var result = ResultFromAnalysis("push", generation.Analysis);
        result.PushedSchemaCount = generation.Analysis.Contracts.Count;
        result.DryRun = _options.DryRun;
        if (_options.DryRun)
        {
            // This builds the exact wire payload without reading credentials or
            // performing network I/O.
            SchemaPushPayloadBuilder.Build(
                generation.Analysis.Contracts,
                "dry-run-revision");
            result.Success = true;
            result.Message =
                $"Validated code-first push bundle with {result.PushedSchemaCount} schemas; no network request was sent.";
            WriteResult(result, writeHumanDiagnostics: true);
            return 0;
        }

        var endpoint = FirstNonEmpty(
            _options.Endpoint,
            configuration.Service.Endpoint,
            Environment.GetEnvironmentVariable("PLAYSERV_API_URL"));
        var keyVariable = string.IsNullOrWhiteSpace(
            configuration.Service.ServerKeyEnvironmentVariable)
            ? "PLAYSERV_SERVER_KEY"
            : configuration.Service.ServerKeyEnvironmentVariable.Trim();
        var serverKey = Environment.GetEnvironmentVariable(keyVariable);
        if (string.IsNullOrWhiteSpace(serverKey))
        {
            throw new InvalidOperationException(
                $"Set {keyVariable} to an sk_* server key before pushing schemas. " +
                "Credentials are intentionally not accepted as command-line arguments or config values.");
        }

        using var client = new SchemaPushClient(
            TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var outcome = client.Push(
            endpoint,
            serverKey,
            configuration.Service.ProjectId,
            configuration.Service.Environment,
            generation.Analysis.Contracts);
        result.Success = true;
        result.PushedSchemaCount = outcome.SchemaCount;
        result.PreviousRevision = outcome.PreviousRevision;
        result.Revision = outcome.Revision;
        result.Message =
            $"Pushed {outcome.SchemaCount} code schemas atomically; revision " +
            $"{outcome.PreviousRevision} -> {outcome.Revision}.";
        WriteResult(result, writeHumanDiagnostics: true);
        return 0;
    }

    private int Doctor()
    {
        var result = BaseResult("doctor");
        var configExists = File.Exists(ProjectConfiguration.GetPath(_options.ProjectRoot));
        result.Success = configExists;
        result.Message =
            $"Runtime={Environment.Version}; OS={Environment.OSVersion}; " +
            $"Config={(configExists ? "found" : "missing")}; " +
            $"Project={_options.ProjectRoot}";
        WriteResult(result);
        return result.Success ? 0 : 1;
    }

    private int WriteVersion()
    {
        var result = BaseResult("version");
        result.Success = true;
        result.Message = ToolConstants.Version;
        WriteResult(result);
        return 0;
    }

    private ToolResult ResultFromAnalysis(
        string command,
        AnalysisSnapshot analysis)
    {
        var result = BaseResult(command);
        result.SourceFileCount = analysis.SourceFiles.Count;
        result.SchemaCount = analysis.Contracts.Count;
        result.Diagnostics = analysis.Diagnostics.ToList();
        return result;
    }

    private ToolResult BaseResult(string command)
    {
        return new ToolResult
        {
            Command = command,
            ProjectRoot = _options.ProjectRoot,
            ConfigurationPath = ProjectConfiguration.GetPath(_options.ProjectRoot)
        };
    }

    private void WriteResult(
        ToolResult result,
        bool writeHumanDiagnostics = false)
    {
        if (_options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, _jsonOptions));
            return;
        }

        Console.WriteLine($"PlayServ Schema Tool {ToolConstants.Version}");
        Console.WriteLine(result.Message);
        if (!writeHumanDiagnostics)
            return;

        foreach (var diagnostic in result.Diagnostics)
        {
            var location = string.IsNullOrWhiteSpace(diagnostic.Path)
                ? string.Empty
                : $"{diagnostic.Path}" +
                  (diagnostic.Line > 0 ? $"({diagnostic.Line})" : string.Empty) +
                  ": ";
            Console.WriteLine(
                $"{location}{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
        }
    }

    private static bool HasErrors(IEnumerable<SchemaDiagnostic> diagnostics)
    {
        return diagnostics.Any(diagnostic =>
            string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase));
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ??
               string.Empty;
    }

    private static void WriteHelp()
    {
        Console.WriteLine(
            """
            PlayServ Schema Tool

            Usage:
              playserv-schema <command> [options]

            Commands:
              init       Create playserv.schema.json.
              status     Report configured sources and discovered schemas.
              analyze    Analyze C# schema contracts without writing files.
              generate   Generate configured outputs and lock file.
              validate   Fail when generated outputs differ from source.
              sync       Generate locally; remote sync activates with the service protocol.
              push       Atomically push attributed schemas through the Schema Service.
              watch      Watch C# sources and regenerate incrementally.
              doctor     Report runtime and configuration diagnostics.
              version    Print tool and protocol versions.

            Options:
              --project <path>   Project root. Defaults to the current directory.
              --changed <path>   IDE hint for a changed file.
              --json             Emit machine-readable JSON.
              --check            Do not write; fail when outputs are stale.
              --dry-run          Validate a push bundle without credentials or network I/O.
              --endpoint <url>   Override service.endpoint or PLAYSERV_API_URL.
              --timeout-seconds  HTTP timeout from 1 through 300 (default 30).
            """);
    }
}

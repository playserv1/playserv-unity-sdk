using System.Text.Json.Serialization;

namespace PlayServ.Schema.Tool;

internal static class ToolConstants
{
    public const string Version = "0.6.9";
    public const int ProtocolVersion = 1;
    public const int ConfigurationVersion = 1;
    public const int LockVersion = 1;
    public const string ConfigurationFileName = "playserv.schema.json";
    public const string LockFileName = "playserv.schema.lock.json";
}

internal sealed class SchemaToolConfiguration
{
    public int SchemaVersion { get; set; } = ToolConstants.ConfigurationVersion;
    public string ProjectId { get; set; } = string.Empty;
    public List<SchemaSourceConfiguration> Sources { get; set; } = new();
    public List<SchemaTargetConfiguration> Targets { get; set; } = new();
    public SchemaServiceConfiguration Service { get; set; } = new();
}

internal sealed class SchemaSourceConfiguration
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "csharp";
    public List<string> Paths { get; set; } = new();
    public string Authority { get; set; } = "contract";
}

internal sealed class SchemaTargetConfiguration
{
    public string Kind { get; set; } = "json-schema";
    public string Output { get; set; } = string.Empty;
    public string Namespace { get; set; } = "PlayServ.Generated.Contracts";
}

internal sealed class SchemaServiceConfiguration
{
    public string Endpoint { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ServerKeyEnvironmentVariable { get; set; } = "PLAYSERV_SERVER_KEY";
}

internal sealed class SchemaContract
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Kind { get; init; } = "object";
    public string Authority { get; init; } = "contract";
    public string Version { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool Singleton { get; init; }
    public string DisplayField { get; init; } = string.Empty;
    public string OwnedBy { get; init; } = string.Empty;
    public string ReadPolicy { get; init; } = string.Empty;
    public string OnPlayerDelete { get; init; } = string.Empty;
    public bool AllowRawFields { get; init; }
    public string ClientRead { get; init; } = string.Empty;
    public string ClientWrite { get; init; } = string.Empty;
    public string ServerRead { get; init; } = string.Empty;
    public string ServerWrite { get; init; } = string.Empty;
    public string BackendRead { get; init; } = string.Empty;
    public string BackendWrite { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public int SourceLine { get; init; }
    public IReadOnlyList<string> FormerNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SchemaMember> Members { get; init; } = Array.Empty<SchemaMember>();
    public IReadOnlyList<string> EnumValues { get; init; } = Array.Empty<string>();
}

internal sealed class SchemaMember
{
    public string Name { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public string FieldType { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string CodeKey { get; init; } = string.Empty;
    public bool Primary { get; init; }
    public bool Unique { get; init; }
    public bool Indexed { get; init; }
    public string Default { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Cardinality { get; init; } = string.Empty;
    public bool Ordered { get; init; }
    public IReadOnlyList<string> FormerNames { get; init; } = Array.Empty<string>();
}

internal sealed class SchemaDiagnostic
{
    public string Severity { get; init; } = "info";
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public int Line { get; init; }
}

internal sealed class AnalysisSnapshot
{
    public IReadOnlyList<string> SourceFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SchemaContract> Contracts { get; init; } =
        Array.Empty<SchemaContract>();
    public IReadOnlyList<SchemaDiagnostic> Diagnostics { get; init; } =
        Array.Empty<SchemaDiagnostic>();
}

internal sealed class GeneratedArtifact
{
    public string Path { get; init; } = string.Empty;

    [JsonIgnore]
    public string Content { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;
}

internal sealed class GenerationSnapshot
{
    public AnalysisSnapshot Analysis { get; init; } = new();
    public IReadOnlyList<GeneratedArtifact> Artifacts { get; init; } =
        Array.Empty<GeneratedArtifact>();
    public string SourceSha256 { get; init; } = string.Empty;
}

internal sealed class SchemaLockFile
{
    public int SchemaVersion { get; set; } = ToolConstants.LockVersion;
    public string ToolVersion { get; set; } = ToolConstants.Version;
    public int ProtocolVersion { get; set; } = ToolConstants.ProtocolVersion;
    public string ProjectId { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public List<SchemaLockSource> Sources { get; set; } = new();
    public List<SchemaLockOutput> Outputs { get; set; } = new();
}

internal sealed class SchemaLockSource
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class SchemaLockOutput
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class ToolResult
{
    public bool Success { get; set; }
    public string Command { get; set; } = string.Empty;
    public string Version { get; set; } = ToolConstants.Version;
    public int ProtocolVersion { get; set; } = ToolConstants.ProtocolVersion;
    public string ProjectRoot { get; set; } = string.Empty;
    public string ConfigurationPath { get; set; } = string.Empty;
    public int SourceFileCount { get; set; }
    public int SchemaCount { get; set; }
    public bool GeneratedFilesChanged { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Outputs { get; set; } = new();
    public List<SchemaDiagnostic> Diagnostics { get; set; } = new();
    public int PushedSchemaCount { get; set; }
    public string PreviousRevision { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public bool DryRun { get; set; }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JsonArray = System.Collections.Generic.List<object?>;
using JsonObject = System.Collections.Generic.Dictionary<string, object?>;

namespace PlayServ.Schema.Tool;

internal static class SchemaGenerator
{
    public static GenerationSnapshot Build(
        string projectRoot,
        SchemaToolConfiguration configuration)
    {
        var analysis = CSharpSchemaAnalyzer.Analyze(projectRoot, configuration);
        var artifacts = new List<GeneratedArtifact>();

        foreach (var target in configuration.Targets)
        {
            var output = NormalizeRelative(target.Output);
            if (string.IsNullOrWhiteSpace(output))
                continue;

            string content;
            if (string.Equals(target.Kind, "json-schema", StringComparison.OrdinalIgnoreCase))
            {
                content = BuildJsonSchema(configuration, analysis.Contracts);
            }
            else if (string.Equals(
                         target.Kind,
                         "csharp-contracts",
                         StringComparison.OrdinalIgnoreCase))
            {
                var outputPath = output.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    ? output
                    : output.TrimEnd('/') + "/PlayServContracts.g.cs";
                output = outputPath;
                content = BuildCSharpContracts(target, analysis.Contracts);
            }
            else
            {
                continue;
            }

            artifacts.Add(new GeneratedArtifact
            {
                Path = output,
                Content = content,
                Sha256 = ComputeSha256(content)
            });
        }

        var sourceSha = ComputeSourceSha(projectRoot, analysis.SourceFiles);
        return new GenerationSnapshot
        {
            Analysis = analysis,
            Artifacts = artifacts
                .OrderBy(artifact => artifact.Path, StringComparer.Ordinal)
                .ToArray(),
            SourceSha256 = sourceSha
        };
    }

    public static bool Apply(
        string projectRoot,
        SchemaToolConfiguration configuration,
        GenerationSnapshot generation,
        bool checkOnly,
        out IReadOnlyList<string> changedPaths)
    {
        var changed = new List<string>();
        foreach (var artifact in generation.Artifacts)
        {
            var absolutePath = Path.GetFullPath(Path.Combine(projectRoot, artifact.Path));
            var current = File.Exists(absolutePath)
                ? File.ReadAllText(absolutePath)
                : null;
            if (string.Equals(current, artifact.Content, StringComparison.Ordinal))
                continue;

            changed.Add(artifact.Path);
            if (!checkOnly)
                ProjectConfiguration.WriteAtomic(absolutePath, artifact.Content);
        }

        var lockContent = BuildLockFile(projectRoot, configuration, generation);
        var lockPath = Path.Combine(projectRoot, ToolConstants.LockFileName);
        var currentLock = File.Exists(lockPath) ? File.ReadAllText(lockPath) : null;
        if (!string.Equals(currentLock, lockContent, StringComparison.Ordinal))
        {
            changed.Add(ToolConstants.LockFileName);
            if (!checkOnly)
                ProjectConfiguration.WriteAtomic(lockPath, lockContent);
        }

        changedPaths = changed;
        return changed.Count > 0;
    }

    private static string BuildJsonSchema(
        SchemaToolConfiguration configuration,
        IReadOnlyList<SchemaContract> contracts)
    {
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = $"urn:playserv:{configuration.ProjectId}:contracts",
            ["title"] = $"{configuration.ProjectId} PlayServ contracts",
            ["x-playserv-project"] = configuration.ProjectId,
            ["x-playserv-tool-version"] = ToolConstants.Version
        };
        var definitions = new JsonObject();
        var contractByType = BuildContractTypeIndex(contracts);

        foreach (var contract in contracts.OrderBy(contract => contract.Id, StringComparer.Ordinal))
        {
            var definition = new JsonObject
            {
                ["title"] = contract.Name,
                ["x-playserv-id"] = contract.Id,
                ["x-playserv-csharp-type"] = contract.FullName,
                ["x-playserv-authority"] = contract.Authority,
                ["x-playserv-source"] = contract.SourceId,
                ["x-playserv-source-path"] = contract.SourcePath
            };
            if (!string.IsNullOrWhiteSpace(contract.Version))
                definition["x-playserv-version"] = contract.Version;
            if (contract.FormerNames.Count > 0)
                definition["x-playserv-former-names"] = ToJsonArray(contract.FormerNames);

            if (contract.Kind == "enum")
            {
                definition["type"] = "string";
                definition["enum"] = ToJsonArray(contract.EnumValues);
            }
            else
            {
                definition["type"] = "object";
                definition["additionalProperties"] = false;
                var properties = new JsonObject();
                var required = new JsonArray();
                foreach (var member in contract.Members)
                {
                    var property = MapType(member.TypeName, contractByType);
                    property["x-playserv-csharp-name"] = member.SourceName;
                    if (member.FormerNames.Count > 0)
                        property["x-playserv-former-names"] = ToJsonArray(member.FormerNames);
                    properties[member.Name] = property;
                    if (member.Required)
                        required.Add(member.Name);
                }

                definition["properties"] = properties;
                if (required.Count > 0)
                    definition["required"] = required;
            }

            definitions[DefinitionKey(contract.Id)] = definition;
        }

        root["$defs"] = definitions;
        return JsonSerializer.Serialize(
                   root,
                   new JsonSerializerOptions { WriteIndented = true }) +
               Environment.NewLine;
    }

    private static string BuildCSharpContracts(
        SchemaTargetConfiguration target,
        IReadOnlyList<SchemaContract> contracts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine();
        builder.Append("namespace ");
        builder.AppendLine(string.IsNullOrWhiteSpace(target.Namespace)
            ? "PlayServ.Generated.Contracts"
            : target.Namespace.Trim());
        builder.AppendLine("{");

        var generatedNames = BuildGeneratedTypeNames(contracts);
        foreach (var contract in contracts.OrderBy(contract => contract.Id, StringComparer.Ordinal))
        {
            var typeName = generatedNames[contract.FullName];
            if (contract.Kind == "enum")
            {
                builder.Append("    public enum ");
                builder.AppendLine(typeName);
                builder.AppendLine("    {");
                for (var index = 0; index < contract.EnumValues.Count; index++)
                {
                    builder.Append("        ");
                    builder.Append(SanitizeIdentifier(contract.EnumValues[index]));
                    builder.AppendLine(index + 1 < contract.EnumValues.Count ? "," : string.Empty);
                }

                builder.AppendLine("    }");
                builder.AppendLine();
                continue;
            }

            builder.AppendLine("    [Serializable]");
            builder.Append("    public sealed partial class ");
            builder.AppendLine(typeName);
            builder.AppendLine("    {");
            foreach (var member in contract.Members)
            {
                builder.Append("        public ");
                builder.Append(MapCSharpType(member.TypeName, generatedNames));
                builder.Append(' ');
                builder.Append(SanitizeIdentifier(member.SourceName));
                builder.AppendLine(" { get; set; }");
            }

            builder.AppendLine("    }");
            builder.AppendLine();
        }

        builder.AppendLine("}");
        builder.AppendLine("#nullable restore");
        return builder.ToString();
    }

    private static IReadOnlyDictionary<string, string> BuildGeneratedTypeNames(
        IReadOnlyList<SchemaContract> contracts)
    {
        var duplicateNames = new HashSet<string>(
            contracts
                .GroupBy(contract => contract.Name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
            StringComparer.Ordinal);
        return contracts.ToDictionary(
            contract => contract.FullName,
            contract => duplicateNames.Contains(contract.Name)
                ? SanitizeIdentifier(contract.Name + "_" + contract.Id)
                : SanitizeIdentifier(contract.Name),
            StringComparer.Ordinal);
    }

    private static string BuildLockFile(
        string projectRoot,
        SchemaToolConfiguration configuration,
        GenerationSnapshot generation)
    {
        var lockFile = new SchemaLockFile
        {
            ProjectId = configuration.ProjectId,
            SourceSha256 = generation.SourceSha256,
            Sources = generation.Analysis.SourceFiles
                .Select(path => new SchemaLockSource
                {
                    Path = path,
                    Sha256 = ComputeFileSha(Path.Combine(projectRoot, path))
                })
                .ToList(),
            Outputs = generation.Artifacts
                .Select(artifact => new SchemaLockOutput
                {
                    Path = artifact.Path,
                    Sha256 = artifact.Sha256
                })
                .ToList()
        };

        return JsonSerializer.Serialize(
                   lockFile,
                   ProjectConfiguration.CreateJsonOptions()) +
               Environment.NewLine;
    }

    private static JsonObject MapType(
        string rawType,
        IReadOnlyDictionary<string, SchemaContract> contractByType)
    {
        var type = NormalizeType(rawType);
        var nullable = type.EndsWith("?", StringComparison.Ordinal);
        if (nullable)
            type = type[..^1].Trim();

        JsonObject schema;
        if (TryGetGeneric(type, "Nullable", out var nullableArguments) &&
            nullableArguments.Count == 1)
        {
            schema = MapType(nullableArguments[0] + "?", contractByType);
            return schema;
        }

        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            schema = new JsonObject
            {
                ["type"] = "array",
                ["items"] = MapType(type[..^2], contractByType)
            };
        }
        else if (TryGetCollectionElement(type, out var elementType))
        {
            schema = new JsonObject
            {
                ["type"] = "array",
                ["items"] = MapType(elementType, contractByType)
            };
        }
        else if (TryGetDictionaryValue(type, out var valueType))
        {
            schema = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = MapType(valueType, contractByType)
            };
        }
        else
        {
            schema = MapScalarOrReference(type, contractByType);
        }

        if (nullable)
        {
            return new JsonObject
            {
                ["anyOf"] = new JsonArray
                {
                    schema,
                    new JsonObject { ["type"] = "null" }
                }
            };
        }

        return schema;
    }

    private static JsonObject MapScalarOrReference(
        string type,
        IReadOnlyDictionary<string, SchemaContract> contractByType)
    {
        var simple = type.Split('.').Last();
        switch (simple)
        {
            case "string":
            case "String":
            case "char":
            case "Char":
                return new JsonObject { ["type"] = "string" };
            case "bool":
            case "Boolean":
                return new JsonObject { ["type"] = "boolean" };
            case "byte":
            case "sbyte":
            case "short":
            case "ushort":
            case "int":
            case "uint":
            case "long":
            case "ulong":
            case "Int16":
            case "Int32":
            case "Int64":
            case "UInt16":
            case "UInt32":
            case "UInt64":
                return new JsonObject { ["type"] = "integer" };
            case "float":
            case "double":
            case "decimal":
            case "Single":
            case "Double":
            case "Decimal":
                return new JsonObject { ["type"] = "number" };
            case "Guid":
                return new JsonObject { ["type"] = "string", ["format"] = "uuid" };
            case "DateTime":
            case "DateTimeOffset":
                return new JsonObject { ["type"] = "string", ["format"] = "date-time" };
            case "object":
            case "Object":
                return new JsonObject();
        }

        if (contractByType.TryGetValue(type, out var exact) ||
            contractByType.TryGetValue(simple, out exact))
        {
            return new JsonObject
            {
                ["$ref"] = "#/$defs/" + DefinitionKey(exact.Id)
            };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["x-playserv-unresolved-csharp-type"] = type
        };
    }

    private static Dictionary<string, SchemaContract> BuildContractTypeIndex(
        IReadOnlyList<SchemaContract> contracts)
    {
        var result = new Dictionary<string, SchemaContract>(StringComparer.Ordinal);
        foreach (var contract in contracts)
        {
            result.TryAdd(contract.FullName, contract);
            result.TryAdd(contract.Name, contract);
        }

        return result;
    }

    private static bool TryGetCollectionElement(string type, out string elementType)
    {
        foreach (var name in new[]
                 {
                     "List", "IList", "IReadOnlyList", "ICollection",
                     "IReadOnlyCollection", "IEnumerable", "HashSet"
                 })
        {
            if (TryGetGeneric(type, name, out var arguments) && arguments.Count == 1)
            {
                elementType = arguments[0];
                return true;
            }
        }

        elementType = string.Empty;
        return false;
    }

    private static bool TryGetDictionaryValue(string type, out string valueType)
    {
        foreach (var name in new[] { "Dictionary", "IDictionary", "IReadOnlyDictionary" })
        {
            if (TryGetGeneric(type, name, out var arguments) && arguments.Count == 2)
            {
                valueType = arguments[1];
                return true;
            }
        }

        valueType = string.Empty;
        return false;
    }

    private static bool TryGetGeneric(
        string type,
        string expectedName,
        out IReadOnlyList<string> arguments)
    {
        var openIndex = type.IndexOf('<');
        var closeIndex = type.LastIndexOf('>');
        if (openIndex <= 0 || closeIndex <= openIndex)
        {
            arguments = Array.Empty<string>();
            return false;
        }

        var name = type[..openIndex].Split('.').Last();
        if (!string.Equals(name, expectedName, StringComparison.Ordinal))
        {
            arguments = Array.Empty<string>();
            return false;
        }

        arguments = SplitGenericArguments(type[(openIndex + 1)..closeIndex]);
        return true;
    }

    private static IReadOnlyList<string> SplitGenericArguments(string value)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(value[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        result.Add(value[start..].Trim());
        return result;
    }

    private static string MapCSharpType(
        string rawType,
        IReadOnlyDictionary<string, string> generatedNames)
    {
        var type = NormalizeType(rawType);
        foreach (var pair in generatedNames.OrderByDescending(pair => pair.Key.Length))
            type = type.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        return type;
    }

    private static string NormalizeType(string type)
    {
        return (type ?? string.Empty)
            .Replace("global::", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string DefinitionKey(string id)
    {
        var builder = new StringBuilder();
        foreach (var character in id)
            builder.Append(char.IsLetterOrDigit(character) || character is '_' or '-'
                ? character
                : '_');
        return builder.Length == 0 ? "Schema" : builder.ToString();
    }

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index == 0 && !char.IsLetter(character) && character != '_')
                builder.Append('_');
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        return builder.Length == 0 ? "SchemaContract" : builder.ToString();
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values)
            result.Add(value);
        return result;
    }

    private static string ComputeSourceSha(
        string projectRoot,
        IReadOnlyList<string> sourceFiles)
    {
        var builder = new StringBuilder();
        foreach (var path in sourceFiles)
        {
            builder.Append(path);
            builder.Append(':');
            builder.Append(ComputeFileSha(Path.Combine(projectRoot, path)));
            builder.Append('\n');
        }

        return ComputeSha256(builder.ToString());
    }

    private static string ComputeFileSha(string path)
    {
        return File.Exists(path)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
            : string.Empty;
    }

    private static string ComputeSha256(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant();
    }

    private static string NormalizeRelative(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim().TrimStart('.', '/');
    }
}

using JsonObject = System.Collections.Generic.Dictionary<string, object?>;

namespace PlayServ.Schema.Tool;

internal static class SchemaPushPayloadBuilder
{
    public static JsonObject Build(
        IReadOnlyList<SchemaContract> contracts,
        string expectedRevision)
    {
        var byType = contracts
            .SelectMany(contract => new[]
            {
                new KeyValuePair<string, SchemaContract>(contract.FullName, contract),
                new KeyValuePair<string, SchemaContract>(contract.Name, contract)
            })
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

        return new JsonObject
        {
            ["enums"] = contracts
                .Where(contract => contract.Kind == "enum")
                .OrderBy(contract => contract.Id, StringComparer.Ordinal)
                .Select(BuildEnum)
                .ToArray(),
            ["parts"] = contracts
                .Where(contract => contract.Kind == "struct")
                .OrderBy(contract => contract.Id, StringComparer.Ordinal)
                .Select(contract => BuildPart(contract, byType))
                .ToArray(),
            ["entities"] = contracts
                .Where(contract => contract.Kind == "object")
                .OrderBy(contract => contract.Id, StringComparer.Ordinal)
                .Select(contract => BuildEntity(contract, byType))
                .ToArray(),
            ["expected_revision"] = expectedRevision ?? string.Empty
        };
    }

    private static JsonObject BuildEnum(SchemaContract contract) => new()
    {
        ["code_key"] = contract.Id,
        ["enum"] = new JsonObject
        {
            ["name"] = contract.Name,
            ["description"] = EmptyToNull(contract.Description),
            ["values"] = contract.EnumValues.ToArray()
        }
    };

    private static JsonObject BuildPart(
        SchemaContract contract,
        IReadOnlyDictionary<string, SchemaContract> byType) => new()
    {
        ["code_key"] = contract.Id,
        ["part"] = new JsonObject
        {
            ["name"] = contract.Name,
            ["description"] = EmptyToNull(contract.Description),
            ["fields"] = contract.Members.Select(member => BuildField(contract, member, byType)).ToArray()
        }
    };

    private static JsonObject BuildEntity(
        SchemaContract contract,
        IReadOnlyDictionary<string, SchemaContract> byType)
    {
        var entity = new JsonObject
        {
            ["name"] = contract.Name,
            ["description"] = EmptyToNull(contract.Description),
            ["display_field"] = EmptyToNull(contract.DisplayField),
            ["singleton"] = contract.Singleton,
            ["owned_by"] = NormalizeOwnedBy(contract.OwnedBy),
            ["read"] = NormalizeReadPolicy(contract.ReadPolicy),
            ["on_player_delete"] = NormalizeDeletePolicy(contract.OnPlayerDelete),
            ["allow_raw_fields"] = contract.AllowRawFields,
            ["fields"] = contract.Members.Select(member => BuildField(contract, member, byType)).ToArray()
        };
        var acl = BuildAcl(contract);
        if (acl != null)
            entity["acl"] = acl;

        return new JsonObject
        {
            ["code_key"] = contract.Id,
            ["entity"] = entity
        };
    }

    private static JsonObject BuildField(
        SchemaContract owner,
        SchemaMember member,
        IReadOnlyDictionary<string, SchemaContract> byType)
    {
        var typeName = UnwrapCollection(TrimNullable(member.TypeName), out var many);
        typeName = TrimNullable(typeName);
        byType.TryGetValue(typeName, out var targetContract);
        var typedReference = SchemaGenerator.TryGetRecordReferenceType(typeName, out var referenceType);
        if (typedReference)
        {
            targetContract = SchemaGenerator.ResolveRecordReferenceTarget(referenceType, byType);
            typeName = referenceType;
            if ((!string.IsNullOrWhiteSpace(member.Target) && member.Target != targetContract.Name) ||
                (!string.IsNullOrWhiteSpace(member.FieldType) && member.FieldType != "Auto" && member.FieldType != "Relation") ||
                (!string.IsNullOrWhiteSpace(member.Cardinality) && member.Cardinality != "Auto" &&
                 member.Cardinality != (many ? "Many" : "One")))
                throw new InvalidOperationException($"Field '{owner.Name}.{member.Name}' conflicts with its typed record reference.");
        }

        var fieldType = ResolveFieldType(typeName, targetContract, member);
        var target = !string.IsNullOrWhiteSpace(member.Target)
            ? member.Target
            : targetContract?.Name;
        var cardinality = NormalizeCardinality(member.Cardinality);
        if ((fieldType == "relation" || fieldType == "inclusion" || fieldType == "reference") &&
            cardinality == null)
        {
            cardinality = many ? "many" : "one";
        }

        return new JsonObject
        {
            ["name"] = member.Name,
            ["type"] = fieldType,
            ["primary"] = member.Primary,
            ["required"] = member.Required,
            ["unique"] = member.Unique,
            ["indexed"] = member.Indexed,
            ["default"] = EmptyToNull(member.Default),
            ["target"] = NeedsTarget(fieldType) ? EmptyToNull(target) : null,
            ["cardinality"] = NeedsCardinality(fieldType) ? cardinality : null,
            ["ordered"] = member.Ordered,
            ["code_key"] = string.IsNullOrWhiteSpace(member.CodeKey)
                ? owner.Id + "." + member.SourceName
                : member.CodeKey
        };
    }

    private static JsonObject? BuildAcl(SchemaContract contract)
    {
        var client = BuildAclSubject(contract.ClientRead, contract.ClientWrite);
        var server = BuildAclSubject(contract.ServerRead, contract.ServerWrite);
        var backend = BuildAclSubject(contract.BackendRead, contract.BackendWrite);
        if (client == null && server == null && backend == null)
            return null;
        return new JsonObject
        {
            ["client"] = client,
            ["server"] = server,
            ["backend"] = backend
        };
    }

    private static JsonObject? BuildAclSubject(string read, string write)
    {
        var readValue = NormalizeAccess(read);
        var writeValue = NormalizeAccess(write);
        if (!readValue.HasValue && !writeValue.HasValue)
            return null;
        return new JsonObject { ["read"] = readValue, ["write"] = writeValue };
    }

    private static string ResolveFieldType(
        string typeName,
        SchemaContract? targetContract,
        SchemaMember member)
    {
        var declared = NormalizeDeclaredFieldType(member.FieldType);
        if (declared != null)
            return declared;

        if (targetContract != null)
        {
            if (targetContract.Kind == "enum") return "enum";
            if (targetContract.Kind == "struct") return "inclusion";
            return "relation";
        }
        if (!string.IsNullOrWhiteSpace(member.Target))
            return "relation";

        return typeName switch
        {
            "string" or "System.String" => "text",
            "bool" or "System.Boolean" => "boolean",
            "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or
                "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or
                "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" => "integer",
            "decimal" or "System.Decimal" => "decimal",
            "float" or "double" or "System.Single" or "System.Double" => "number",
            "DateTime" or "System.DateTime" or "DateTimeOffset" or "System.DateTimeOffset" => "datetime",
            "Guid" or "System.Guid" => "uuid",
            "Uri" or "System.Uri" => "url",
            _ => "json"
        };
    }

    private static string? NormalizeDeclaredFieldType(string value) => value switch
    {
        "Text" => "text",
        "LongText" => "longtext",
        "Integer" => "integer",
        "Decimal" => "decimal",
        "Number" => "number",
        "Boolean" => "boolean",
        "DateTime" => "datetime",
        "Date" => "date",
        "Email" => "email",
        "Url" => "url",
        "Uuid" => "uuid",
        "Json" => "json",
        "Enum" => "enum",
        "Reference" => "reference",
        "Relation" => "relation",
        "Inclusion" => "inclusion",
        _ => null
    };

    private static string UnwrapCollection(string typeName, out bool many)
    {
        typeName = (typeName ?? string.Empty).Trim();
        if (typeName.EndsWith("[]", StringComparison.Ordinal))
        {
            many = true;
            return typeName[..^2].Trim();
        }

        var open = typeName.IndexOf('<');
        if (open > 0 && typeName.EndsWith(">", StringComparison.Ordinal))
        {
            var outer = typeName[..open].Split('.').Last();
            if (outer is "List" or "IList" or "IReadOnlyList" or "ICollection" or
                "IReadOnlyCollection" or "IEnumerable" or "HashSet")
            {
                many = true;
                return typeName[(open + 1)..^1].Trim();
            }
        }

        many = false;
        return typeName;
    }

    private static string TrimNullable(string typeName) =>
        typeName.EndsWith("?", StringComparison.Ordinal) ? typeName[..^1].Trim() : typeName;

    private static bool NeedsTarget(string type) =>
        type is "enum" or "reference" or "relation" or "inclusion";

    private static bool NeedsCardinality(string type) =>
        type is "reference" or "relation" or "inclusion";

    private static string? NormalizeCardinality(string value) => value switch
    {
        "One" => "one",
        "Many" => "many",
        _ => null
    };

    private static string? NormalizeOwnedBy(string value) => value == "Player" ? "player" : null;

    private static string? NormalizeReadPolicy(string value) => value switch
    {
        "Owner" => "owner",
        "Public" => "public",
        _ => null
    };

    private static string? NormalizeDeletePolicy(string value) => value switch
    {
        "CascadeDelete" => "cascade-delete",
        "Restrict" => "restrict",
        "Anonymise" => "anonymise",
        _ => null
    };

    private static bool? NormalizeAccess(string value) => value switch
    {
        "Allow" => true,
        "Deny" => false,
        _ => null
    };

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

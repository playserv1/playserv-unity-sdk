using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Playserv.Deploy.Editor.Analysis.Validators
{
    public sealed class DependencyReferenceValidator : SyntaxValidator
    {
        private static readonly HashSet<string> AllowedSystemTypes = new HashSet<string>
        {
            "System.String",
            "System.Int32",
            "System.Int64",
            "System.Boolean",
            "System.Double",
            "System.Float",
            "System.Decimal",
            "System.DateTime",
            "System.DateTimeOffset",
            "System.Guid",
            "System.TimeSpan",
            "System.Byte",
            "System.SByte",
            "System.Int16",
            "System.UInt16",
            "System.UInt32",
            "System.UInt64",
            "System.Char",
            "System.Collections.Generic.List",
            "System.Collections.Generic.Dictionary",
            "System.Collections.Generic.HashSet",
            "System.Collections.Generic.IEnumerable",
            "System.Collections.Generic.IList",
            "System.Collections.Generic.IDictionary",
            "System.Nullable"
        };

        public override ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel)
        {
            var result = new ValidationResult { IsValid = true };

            foreach (var node in root.DescendantNodes())
            {
                var classDecl = node as ClassDeclarationSyntax;
                if (classDecl != null)
                {
                    ValidateTypeReferences(classDecl, classDecl.Identifier.Text, "class", result);
                    continue;
                }

                var structDecl = node as StructDeclarationSyntax;
                if (structDecl != null)
                {
                    ValidateTypeReferences(structDecl, structDecl.Identifier.Text, "struct", result);
                    continue;
                }

                var recordDecl = node as RecordDeclarationSyntax;
                if (recordDecl != null)
                    ValidateTypeReferences(recordDecl, recordDecl.Identifier.Text, "record", result);
            }

            return result;
        }

        private static void ValidateTypeReferences(
            TypeDeclarationSyntax node,
            string typeName,
            string typeKind,
            ValidationResult result)
        {
            if (node.BaseList != null)
            {
                foreach (var baseType in node.BaseList.Types)
                {
                    var baseTypeName = baseType.Type.ToString();

                    if (baseTypeName.IndexOf(".", StringComparison.Ordinal) < 0)
                        continue;

                    if (!IsAllowedType(baseTypeName))
                    {
                        AddError(
                            result,
                            "Dependency " + typeKind + " '" + typeName +
                            "' cannot inherit from or implement external type '" + baseTypeName +
                            "'. Only user-defined types are allowed");
                    }
                }
            }

            foreach (var property in node.Members.OfType<PropertyDeclarationSyntax>())
                ValidatePropertyType(property, typeName, typeKind, result);

            foreach (var field in node.Members.OfType<FieldDeclarationSyntax>())
                ValidateFieldType(field, typeName, typeKind, result);
        }

        private static void ValidatePropertyType(
            PropertyDeclarationSyntax property,
            string typeName,
            string typeKind,
            ValidationResult result)
        {
            var propertyType = property.Type.ToString();
            var propertyName = property.Identifier.Text;

            ValidateType(propertyType, typeName, typeKind, "property '" + propertyName + "'", result);
        }

        private static void ValidateFieldType(
            FieldDeclarationSyntax field,
            string typeName,
            string typeKind,
            ValidationResult result)
        {
            var fieldType = field.Declaration.Type.ToString();

            foreach (var variable in field.Declaration.Variables)
            {
                var fieldName = variable.Identifier.Text;
                ValidateType(fieldType, typeName, typeKind, "field '" + fieldName + "'", result);
            }
        }

        private static void ValidateType(
            string type,
            string typeName,
            string typeKind,
            string memberDescription,
            ValidationResult result)
        {
            var baseType = ExtractBaseType(type);

            if (baseType.IndexOf(".", StringComparison.Ordinal) < 0 && !IsGenericCollection(type))
                return;

            if (!IsAllowedType(baseType))
            {
                AddError(
                    result,
                    "Dependency " + typeKind + " '" + typeName + "' " + memberDescription +
                    " uses external type '" + type + "'. Only user-defined types and basic BCL types are allowed");
            }
        }

        private static string ExtractBaseType(string type)
        {
            var genericIndex = type.IndexOf('<');
            if (genericIndex > 0)
                return type.Substring(0, genericIndex);

            if (type.EndsWith("?", StringComparison.Ordinal))
                return type.Substring(0, type.Length - 1);

            return type;
        }

        private static bool IsGenericCollection(string type)
        {
            return type.StartsWith("List<", StringComparison.Ordinal) ||
                   type.StartsWith("Dictionary<", StringComparison.Ordinal) ||
                   type.StartsWith("HashSet<", StringComparison.Ordinal) ||
                   type.StartsWith("IEnumerable<", StringComparison.Ordinal) ||
                   type.StartsWith("IList<", StringComparison.Ordinal) ||
                   type.StartsWith("IDictionary<", StringComparison.Ordinal);
        }

        private static bool IsAllowedType(string type)
        {
            if (AllowedSystemTypes.Contains(type))
                return true;

            if (type.IndexOf(".", StringComparison.Ordinal) < 0)
                return true;

            return AllowedSystemTypes.Any(allowed => type.StartsWith(allowed, StringComparison.Ordinal));
        }

        private static void AddError(ValidationResult result, string error)
        {
            result.IsValid = false;
            result.Errors.Add(error);
        }
    }
}

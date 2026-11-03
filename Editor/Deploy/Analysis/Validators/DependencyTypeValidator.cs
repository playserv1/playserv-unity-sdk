using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Playserv.Deploy.Editor.Analysis.Validators
{
    public sealed class DependencyTypeValidator : SyntaxValidator
    {
        private static readonly HashSet<string> AllowedTypesWithMethods = new HashSet<string> { "Result" };

        public override ValidationResult Validate(SyntaxNode root, SemanticModel semanticModel)
        {
            var result = new ValidationResult { IsValid = true };

            foreach (var node in root.DescendantNodes())
            {
                var classDecl = node as ClassDeclarationSyntax;
                if (classDecl != null)
                {
                    ValidateClass(classDecl, result);
                    continue;
                }

                var structDecl = node as StructDeclarationSyntax;
                if (structDecl != null)
                {
                    ValidateStruct(structDecl, result);
                    continue;
                }

                var enumDecl = node as EnumDeclarationSyntax;
                if (enumDecl != null)
                {
                    ValidateEnum(enumDecl, result);
                    continue;
                }

                var recordDecl = node as RecordDeclarationSyntax;
                if (recordDecl != null)
                {
                    ValidateRecord(recordDecl, result);
                    continue;
                }

                var interfaceDecl = node as InterfaceDeclarationSyntax;
                if (interfaceDecl != null)
                    ValidateInterface(interfaceDecl, result);
            }

            return result;
        }

        private static void ValidateClass(ClassDeclarationSyntax node, ValidationResult result)
        {
            var className = node.Identifier.Text;

            if (AllowedTypesWithMethods.Contains(className))
                return;

            if (!node.Modifiers.Any(m => m.Text == "public"))
                AddError(result, "Dependency class '" + className + "' must be public");

            if (node.Modifiers.Any(m => m.Text == "static"))
                AddError(result, "Dependency class '" + className + "' cannot be static");

            if (node.Modifiers.Any(m => m.Text == "abstract"))
                AddError(result, "Dependency class '" + className + "' cannot be abstract");

            if (node.TypeParameterList != null && node.TypeParameterList.Parameters.Count > 0)
            {
                AddError(result, "Dependency class '" + className + "' cannot have generic type parameters");
            }

            if (node.Parent is ClassDeclarationSyntax)
                AddError(result, "Dependency class '" + className + "' cannot be nested inside another class");

            var methods = node.Members.OfType<MethodDeclarationSyntax>().ToList();
            if (methods.Count > 0)
            {
                AddError(
                    result,
                    "Dependency class '" + className + "' cannot have methods. Dependencies must be simple data classes (POCOs)");
            }
        }

        private static void ValidateStruct(StructDeclarationSyntax node, ValidationResult result)
        {
            var structName = node.Identifier.Text;

            if (!node.Modifiers.Any(m => m.Text == "public"))
                AddError(result, "Dependency struct '" + structName + "' must be public");

            if (node.TypeParameterList != null && node.TypeParameterList.Parameters.Count > 0)
            {
                AddError(result, "Dependency struct '" + structName + "' cannot have generic type parameters");
            }

            var methods = node.Members.OfType<MethodDeclarationSyntax>().ToList();
            if (methods.Count > 0)
            {
                AddError(
                    result,
                    "Dependency struct '" + structName +
                    "' cannot have methods. Dependencies must be simple data structures");
            }
        }

        private static void ValidateEnum(EnumDeclarationSyntax node, ValidationResult result)
        {
            var enumName = node.Identifier.Text;

            if (!node.Modifiers.Any(m => m.Text == "public"))
                AddError(result, "Dependency enum '" + enumName + "' must be public");
        }

        private static void ValidateRecord(RecordDeclarationSyntax node, ValidationResult result)
        {
            var recordName = node.Identifier.Text;

            if (!node.Modifiers.Any(m => m.Text == "public"))
                AddError(result, "Dependency record '" + recordName + "' must be public");

            if (node.TypeParameterList != null && node.TypeParameterList.Parameters.Count > 0)
            {
                AddError(result, "Dependency record '" + recordName + "' cannot have generic type parameters");
            }

            var methods = node.Members.OfType<MethodDeclarationSyntax>().ToList();
            if (methods.Count > 0)
            {
                AddError(
                    result,
                    "Dependency record '" + recordName + "' cannot have methods. Dependencies must be simple data records");
            }
        }

        private static void ValidateInterface(InterfaceDeclarationSyntax node, ValidationResult result)
        {
            var interfaceName = node.Identifier.Text;

            if (!node.Modifiers.Any(m => m.Text == "public"))
                AddError(result, "Dependency interface '" + interfaceName + "' must be public");

            if (node.TypeParameterList != null && node.TypeParameterList.Parameters.Count > 0)
            {
                AddError(result, "Dependency interface '" + interfaceName + "' cannot have generic type parameters");
            }
        }

        private static void AddError(ValidationResult result, string error)
        {
            result.IsValid = false;
            result.Errors.Add(error);
        }
    }
}

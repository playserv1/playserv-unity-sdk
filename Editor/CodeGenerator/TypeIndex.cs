#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.CodeGenerator.Editor
{
    internal enum TypeReferenceResolutionKind
    {
        Unknown,
        Exact,
        UniqueShortName,
        AmbiguousShortName
    }

    internal sealed class TypeReferenceResolution
    {
        public TypeReferenceResolutionKind Kind { get; }
        public string? FullName { get; }
        public IReadOnlyList<string> Candidates { get; }

        public bool IsResolved => !string.IsNullOrWhiteSpace(FullName);
        public bool IsAmbiguous => Kind == TypeReferenceResolutionKind.AmbiguousShortName;

        public TypeReferenceResolution(TypeReferenceResolutionKind kind, string? fullName, IReadOnlyList<string>? candidates = null)
        {
            Kind = kind;
            FullName = fullName;
            Candidates = candidates ?? Array.Empty<string>();
        }
    }

    internal sealed class TypeIndex
    {
        private readonly Dictionary<string, TypeInfoModel> _types;
        private readonly HashSet<string> _enums;

        // enum short -> enum full
        private readonly Dictionary<string, string> _enumFullNames;
        private readonly Dictionary<string, List<string>> _typeCandidatesByShortName;
        private readonly Dictionary<string, List<string>> _enumCandidatesByShortName;

        public TypeIndex(
            Dictionary<string, TypeInfoModel> types,
            HashSet<string> enums,
            Dictionary<string, string> enumFullNames,
            Dictionary<string, List<string>> typeCandidatesByShortName,
            Dictionary<string, List<string>> enumCandidatesByShortName)
        {
            _types = types;
            _enums = enums;
            _enumFullNames = enumFullNames;
            _typeCandidatesByShortName = typeCandidatesByShortName;
            _enumCandidatesByShortName = enumCandidatesByShortName;
        }

        public TypeInfoModel? Find(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            var resolution = ResolveReference(typeName);
            if (!resolution.IsResolved || string.IsNullOrWhiteSpace(resolution.FullName))
                return null;

            return _types.TryGetValue(resolution.FullName, out var resolvedType)
                ? resolvedType
                : null;
        }

        public bool IsEnum(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            var resolution = ResolveReference(typeName);
            return resolution.IsResolved &&
                   !string.IsNullOrWhiteSpace(resolution.FullName) &&
                   _enums.Contains(resolution.FullName);
        }

        public string? TryGetFullName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            var resolution = ResolveReference(typeName);
            return resolution.IsResolved ? resolution.FullName : null;
        }

        public TypeReferenceResolution ResolveReference(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return new TypeReferenceResolution(TypeReferenceResolutionKind.Unknown, null);

            var normalized = StripGlobalPrefix(typeName.Trim());
            if (string.IsNullOrWhiteSpace(normalized))
                return new TypeReferenceResolution(TypeReferenceResolutionKind.Unknown, null);

            if (normalized.IndexOf('.', StringComparison.Ordinal) >= 0)
            {
                if (_types.TryGetValue(normalized, out var exactType))
                    return new TypeReferenceResolution(TypeReferenceResolutionKind.Exact, exactType.FullName, new[] { exactType.FullName });

                if (_enums.Contains(normalized))
                    return new TypeReferenceResolution(TypeReferenceResolutionKind.Exact, normalized, new[] { normalized });

                return new TypeReferenceResolution(TypeReferenceResolutionKind.Unknown, null);
            }

            var shortName = normalized.Split('.').Last();

            if (_typeCandidatesByShortName.TryGetValue(shortName, out var typeCandidates) &&
                typeCandidates != null &&
                typeCandidates.Count > 0)
            {
                if (typeCandidates.Count == 1)
                    return new TypeReferenceResolution(TypeReferenceResolutionKind.UniqueShortName, typeCandidates[0], typeCandidates);

                return new TypeReferenceResolution(TypeReferenceResolutionKind.AmbiguousShortName, null, typeCandidates);
            }

            if (_enumCandidatesByShortName.TryGetValue(shortName, out var enumCandidates) &&
                enumCandidates != null &&
                enumCandidates.Count > 0)
            {
                if (enumCandidates.Count == 1)
                    return new TypeReferenceResolution(TypeReferenceResolutionKind.UniqueShortName, enumCandidates[0], enumCandidates);

                return new TypeReferenceResolution(TypeReferenceResolutionKind.AmbiguousShortName, null, enumCandidates);
            }

            return new TypeReferenceResolution(TypeReferenceResolutionKind.Unknown, null);
        }

        private static string StripGlobalPrefix(string typeName)
        {
            const string prefix = "global::";
            return !string.IsNullOrWhiteSpace(typeName) &&
                   typeName.StartsWith(prefix, StringComparison.Ordinal)
                ? typeName.Substring(prefix.Length)
                : typeName;
        }
    }
}
#nullable restore

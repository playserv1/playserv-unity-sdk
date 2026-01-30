#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.CodeGenerator.Editor
{
    internal sealed class TypeIndex
    {
        private readonly Dictionary<string, TypeInfoModel> _types;
        private readonly HashSet<string> _enums;

        // enum short -> enum full
        private readonly Dictionary<string, string> _enumFullNames;

        public TypeIndex(
            Dictionary<string, TypeInfoModel> types,
            HashSet<string> enums,
            Dictionary<string, string> enumFullNames)
        {
            _types = types;
            _enums = enums;
            _enumFullNames = enumFullNames;
        }

        public TypeInfoModel? Find(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            if (_types.TryGetValue(typeName, out var t))
                return t;

            var shortName = typeName.Split('.').Last();
            if (_types.TryGetValue(shortName, out var t2))
                return t2;

            return null;
        }

        public bool IsEnum(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            if (_enums.Contains(typeName))
                return true;

            var shortName = typeName.Split('.').Last();
            return _enums.Contains(shortName);
        }

        public string? TryGetFullName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            // If it's enum and we know exact full name -> return it
            if (_enumFullNames.TryGetValue(typeName, out var enumFull))
                return enumFull;

            var t = Find(typeName);
            return t != null ? t.FullName : null;
        }
    }
}
#nullable restore
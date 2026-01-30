#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playserv.CodeGenerator.Editor
{
    internal sealed class TypeInfoModel
    {
        public string Name { get; }
        public string FullName { get; }

        private readonly Dictionary<string, string> _memberTypes;

        public TypeInfoModel(string name, string fullName, Dictionary<string, string> memberTypes)
        {
            Name = name;
            FullName = fullName;
            _memberTypes = memberTypes;
        }

        public string? TryGetMemberType(string memberName)
        {
            if (string.IsNullOrWhiteSpace(memberName))
                return null;

            if (_memberTypes.TryGetValue(memberName, out var t))
                return t;

            var hit = _memberTypes.FirstOrDefault(kv => kv.Key.Equals(memberName, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(hit.Key) ? null : hit.Value;
        }

        public bool TryResolveMember(string memberName, out string resolvedName, out string resolvedType)
        {
            resolvedName = memberName;
            resolvedType = "";

            if (string.IsNullOrWhiteSpace(memberName))
                return false;

            if (_memberTypes.TryGetValue(memberName, out var t))
            {
                resolvedType = t;
                return true;
            }

            foreach (var kv in _memberTypes)
            {
                if (kv.Key.Equals(memberName, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedName = kv.Key;
                    resolvedType = kv.Value;
                    return true;
                }
            }

            return false;
        }
    }
}
#nullable restore
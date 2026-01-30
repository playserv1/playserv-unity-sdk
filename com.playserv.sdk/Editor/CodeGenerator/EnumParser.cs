using System;
using System.Collections.Generic;

namespace Playserv.CodeGenerator.Editor
{
    internal static class EnumParser
    {
        public static IEnumerable<string> FindEnums(string src)
        {
            bool inString = false;

            for (int i = 0; i < src.Length - 5; i++)
            {
                char c = src[i];

                if (c == '"' && (i == 0 || src[i - 1] != '\\'))
                    inString = !inString;

                if (inString)
                    continue;

                if (src[i] == 'e' && src.IndexOf("enum ", i, StringComparison.Ordinal) == i)
                {
                    int nameStart = i + "enum ".Length;
                    if (nameStart >= src.Length) yield break;

                    var rest = src.Substring(nameStart);
                    var name = TypeNameUtil.TakeIdentifier(rest);

                    if (!string.IsNullOrWhiteSpace(name))
                        yield return name;

                    i = nameStart + name.Length;
                }
            }
        }
    }
}
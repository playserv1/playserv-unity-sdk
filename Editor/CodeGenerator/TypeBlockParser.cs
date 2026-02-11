using System;
using System.Collections.Generic;

namespace Playserv.CodeGenerator.Editor
{
    internal static class TypeBlockParser
    {
        internal sealed class TypeBlock
        {
            public string TypeName = "";
            public string Body = "";
        }

        public static IEnumerable<TypeBlock> FindTypeBlocks(string src)
        {
            var keywords = new[] { "class", "struct", "record" };

            int i = 0;
            while (i < src.Length)
            {
                int best = -1;
                string bestKw = "";

                foreach (var kw in keywords)
                {
                    var idx = src.IndexOf(kw + " ", i, StringComparison.Ordinal);
                    if (idx >= 0 && (best < 0 || idx < best))
                    {
                        best = idx;
                        bestKw = kw;
                    }
                }

                if (best < 0)
                    yield break;

                int nameStart = best + bestKw.Length + 1;
                var after = src.Substring(nameStart);
                var typeName = TypeNameUtil.TakeIdentifier(after);

                int braceOpen = src.IndexOf('{', nameStart);
                if (braceOpen < 0)
                {
                    i = nameStart + typeName.Length;
                    continue;
                }

                int braceClose = FindMatchingBrace(src, braceOpen);
                if (braceClose < 0)
                {
                    i = braceOpen + 1;
                    continue;
                }

                var body = src.Substring(braceOpen + 1, braceClose - braceOpen - 1);

                yield return new TypeBlock
                {
                    TypeName = typeName,
                    Body = body
                };

                i = braceClose + 1;
            }
        }

        private static int FindMatchingBrace(string src, int openPos)
        {
            int depth = 0;
            bool inString = false;

            for (int i = openPos; i < src.Length; i++)
            {
                char c = src[i];

                if (c == '"' && (i == 0 || src[i - 1] != '\\'))
                    inString = !inString;

                if (inString) continue;

                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
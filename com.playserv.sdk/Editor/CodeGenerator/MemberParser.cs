#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Playserv.CodeGenerator.Editor
{
    internal static class MemberParser
    {
        // Supports:
        // - fields: public T Name; / public T Name = ...;
        // - props:  public T Name { get; set; }
        //          public T Name { get; init; }
        //          public T Name { get; }
        //          with optional "= ...;" initializer
        // - optional "required" keyword (C# 11)
        // - generics with namespaces, nullable, arrays
        public static Dictionary<string, string> ParsePublicMembers(string typeBody)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(typeBody))
                return map;

            // Remove comments to avoid false positives.
            var src = StripComments(typeBody);

            int i = 0;
            while (i < src.Length)
            {
                // Find "public"
                int p = IndexOfKeyword(src, "public", i);
                if (p < 0)
                    break;

                i = p + "public".Length;

                // Skip whitespace
                SkipWs(src, ref i);

                // Optional: "required"
                if (IsKeywordAt(src, i, "required"))
                {
                    i += "required".Length;
                    SkipWs(src, ref i);
                }

                // Read type token(s) until identifier (member name) begins.
                // We read a "type" which may include generics and dots.
                var type = ReadType(src, ref i);
                if (string.IsNullOrWhiteSpace(type))
                {
                    // Ensure forward progress (e.g. when encountering verbatim identifiers like @Class)
                    i = Math.Min(i + 1, src.Length);
                    continue;
                }

                SkipWs(src, ref i);

                // Read member name
                var name = ReadIdentifier(src, ref i);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                SkipWs(src, ref i);

                // Field: ends with ';' or '=' initializer then ';'
                if (i < src.Length && (src[i] == ';' || src[i] == '='))
                {
                    // Skip initializer if present
                    if (src[i] == '=')
                    {
                        // consume until ';' at top-level (not inside strings)
                        ConsumeUntilSemicolon(src, ref i);
                    }
                    else
                    {
                        i++; // consume ';'
                    }

                    map[name] = type.Trim();
                    continue;
                }

                // Property: expect '{'
                if (i < src.Length && src[i] == '{')
                {
                    // Consume property block quickly
                    // We accept any property with "get;" inside.
                    int blockStart = i;
                    if (TryConsumeBracesBlock(src, ref i, out var blockText))
                    {
                        if (blockText.Contains("get;", StringComparison.Ordinal))
                        {
                            map[name] = type.Trim();
                        }

                        // Optional initializer after property block: "= ...;"
                        SkipWs(src, ref i);
                        if (i < src.Length && src[i] == '=')
                            ConsumeUntilSemicolon(src, ref i);

                        continue;
                    }
                }

                // If we got here, skip to next semicolon or newline to avoid infinite loop
                SkipToNextLineOrSemicolon(src, ref i);
            }

            return map;
        }

        private static string ReadType(string s, ref int i)
        {
            //  Reads a type like:
            //  int
            //  List<Item>
            //  System.Collections.Generic.List<Shared.Generated.Models.Item>
            //  IReadOnlyList<Item>?
            //  Item[]
            //  Dictionary<string, List<Item>>
            var sb = new StringBuilder();
            int depth = 0;

            while (i < s.Length)
            {
                char c = s[i];

                if (char.IsWhiteSpace(c) && depth == 0)
                    break;

                if (c == '<') depth++;
                if (c == '>') depth = Math.Max(0, depth - 1);

                // Stop before member name: when depth==0 and next token is identifier that looks like name,
                // but we don't know that yet. We rely on whitespace split above.

                // Allow letters/digits/underscore/dot/question/array brackets/commas/spaces inside generics
                if (IsTypeChar(c) || (depth > 0 && (c == ' ')))
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                // If type is finished (like "int") and next is identifier start, we will stop on whitespace anyway.
                // For safety, stop on characters that can't be in type at depth 0.
                if (depth == 0)
                    break;

                sb.Append(c);
                i++;
            }

            return sb.ToString().Trim();
        }

        private static bool IsTypeChar(char c)
        {
            return char.IsLetterOrDigit(c)
                   || c == '_'
                   || c == '.'
                   || c == '?'
                   || c == '['
                   || c == ']'
                   || c == ','
                   || c == '<'
                   || c == '>'
                   || c == '@';
        }

        private static string ReadIdentifier(string s, ref int i)
        {
            if (i >= s.Length)
                return "";

            //Allow verbatim identifiers like @Class
            bool hasAt = s[i] == '@';
            if (hasAt)
            {
                i++;
                if (i >= s.Length)
                    return "";
            }

            if (!(char.IsLetter(s[i]) || s[i] == '_'))
                return "";

            var start = hasAt ? i - 1 : i;
            i++;

            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                i++;

            return s.Substring(start, i - start);
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        private static bool IsKeywordAt(string s, int i, string keyword)
        {
            if (i < 0 || i + keyword.Length > s.Length)
                return false;

            if (!s.AsSpan(i, keyword.Length).SequenceEqual(keyword))
                return false;

            bool beforeOk = i == 0 || !IsIdentChar(s[i - 1]);
            int afterPos = i + keyword.Length;
            bool afterOk = afterPos >= s.Length || !IsIdentChar(s[afterPos]);

            return beforeOk && afterOk;
        }

        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@';

        private static int IndexOfKeyword(string s, string keyword, int start)
        {
            for (int i = start; i <= s.Length - keyword.Length; i++)
            {
                if (!s.AsSpan(i, keyword.Length).SequenceEqual(keyword))
                    continue;

                bool beforeOk = i == 0 || !IsIdentChar(s[i - 1]);
                int afterPos = i + keyword.Length;
                bool afterOk = afterPos >= s.Length || !IsIdentChar(s[afterPos]);

                if (beforeOk && afterOk)
                    return i;
            }
            return -1;
        }

        private static bool TryConsumeBracesBlock(string s, ref int i, out string block)
        {
            block = "";
            if (i >= s.Length || s[i] != '{')
                return false;

            int start = i;
            int depth = 0;
            bool inString = false;

            while (i < s.Length)
            {
                char c = s[i];

                if (c == '"' && (i == 0 || s[i - 1] != '\\'))
                    inString = !inString;

                if (!inString)
                {
                    if (c == '{') depth++;
                    if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            i++; // consume closing '}'
                            block = s.Substring(start, i - start);
                            return true;
                        }
                    }
                }

                i++;
            }

            return false;
        }

        private static void ConsumeUntilSemicolon(string s, ref int i)
        {
            // assumes current char is '='
            bool inString = false;

            while (i < s.Length)
            {
                char c = s[i];

                if (c == '"' && (i == 0 || s[i - 1] != '\\'))
                    inString = !inString;

                if (!inString && c == ';')
                {
                    i++; // consume ';'
                    return;
                }

                i++;
            }
        }

        private static void SkipToNextLineOrSemicolon(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                i++;
                if (c == '\n' || c == ';')
                    return;
            }
        }

        private static string StripComments(string s)
        {
            // Strips both // and /* */ comments, safe enough for parsing signatures.
            var sb = new StringBuilder(s.Length);
            bool inString = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c == '"' && (i == 0 || s[i - 1] != '\\'))
                    inString = !inString;

                if (!inString && c == '/' && i + 1 < s.Length)
                {
                    char n = s[i + 1];

                    // line comment //
                    if (n == '/')
                    {
                        i += 2;
                        while (i < s.Length && s[i] != '\n') i++;
                        if (i < s.Length) sb.Append('\n');
                        continue;
                    }

                    // block comment /* */
                    if (n == '*')
                    {
                        i += 2;
                        while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                        i++; // skip '*'
                        continue;
                    }
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
#nullable restore
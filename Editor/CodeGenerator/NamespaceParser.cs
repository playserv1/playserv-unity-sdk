#nullable enable
using System;
using System.Text;

namespace Playserv.CodeGenerator.Editor
{
    internal static class NamespaceParser
    {
        public static string ExtractNamespace(string src)
        {
            if (string.IsNullOrWhiteSpace(src))
                return "";
            
            // in comments, verbatim strings, normal strings, etc.
            var cleaned = StripCommentsAndStrings(src);

            // Find first "namespace" keyword (supports file-scoped and block-scoped)
            var idx = IndexOfKeyword(cleaned, "namespace");
            if (idx < 0)
                return "";

            idx += "namespace".Length;

            // Skip whitespace
            while (idx < cleaned.Length && char.IsWhiteSpace(cleaned[idx]))
                idx++;

            if (idx >= cleaned.Length)
                return "";

            // Read namespace identifier: allow letters/digits/_ and dot
            var sb = new StringBuilder();
            while (idx < cleaned.Length)
            {
                var c = cleaned[idx];
                if (IsNsChar(c))
                {
                    sb.Append(c);
                    idx++;
                    continue;
                }
                break;
            }

            var ns = sb.ToString().Trim();
            return IsValidNamespace(ns) ? ns : "";
        }

        private static string StripCommentsAndStrings(string s)
        {
            var sb = new StringBuilder(s.Length);

            bool inLineComment = false;
            bool inBlockComment = false;

            bool inString = false;          // "..."
            bool inVerbatimString = false;  // @"..."
            bool inChar = false;            // 'a'

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                char next = (i + 1 < s.Length) ? s[i + 1] : '\0';

                // --- handle exiting line comment ---
                if (inLineComment)
                {
                    if (c == '\n')
                    {
                        inLineComment = false;
                        sb.Append('\n');
                    }
                    else
                    {
                        // keep structure but remove content
                        sb.Append(' ');
                    }
                    continue;
                }

                // --- handle exiting block comment ---
                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        sb.Append("  ");
                        i++; // consume '/'
                    }
                    else
                    {
                        sb.Append(c == '\n' ? '\n' : ' ');
                    }
                    continue;
                }

                // --- handle char literal ---
                if (inChar)
                {
                    // handle escape
                    if (c == '\\' && next != '\0')
                    {
                        sb.Append("  ");
                        i++;
                        continue;
                    }

                    if (c == '\'')
                        inChar = false;

                    sb.Append(' ');
                    continue;
                }

                // --- handle verbatim string @"..." ---
                if (inVerbatimString)
                {
                    // verbatim escape is "" (double quote)
                    if (c == '"' && next == '"')
                    {
                        sb.Append("  ");
                        i++;
                        continue;
                    }

                    if (c == '"')
                        inVerbatimString = false;

                    sb.Append(' ');
                    continue;
                }

                // --- handle normal string "..." ---
                if (inString)
                {
                    // handle escape
                    if (c == '\\' && next != '\0')
                    {
                        sb.Append("  ");
                        i++;
                        continue;
                    }

                    if (c == '"')
                        inString = false;

                    sb.Append(' ');
                    continue;
                }

                // --- detect comment starts ---
                if (c == '/' && next == '/')
                {
                    inLineComment = true;
                    sb.Append("  ");
                    i++;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    sb.Append("  ");
                    i++;
                    continue;
                }

                // --- detect string / char starts ---
                if (c == '@' && next == '"')
                {
                    inVerbatimString = true;
                    sb.Append("  ");
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    sb.Append(' ');
                    continue;
                }

                if (c == '\'')
                {
                    inChar = true;
                    sb.Append(' ');
                    continue;
                }

                // Keep actual code chars
                sb.Append(c);
            }

            return sb.ToString();
        }

        private static int IndexOfKeyword(string s, string keyword)
        {
            for (int i = 0; i <= s.Length - keyword.Length; i++)
            {
                if (!s.AsSpan(i, keyword.Length).SequenceEqual(keyword))
                    continue;

                var beforeOk = i == 0 || !IsIdentChar(s[i - 1]);
                var afterPos = i + keyword.Length;
                var afterOk = afterPos >= s.Length || !IsIdentChar(s[afterPos]);

                if (beforeOk && afterOk)
                    return i;
            }

            return -1;
        }

        private static bool IsNsChar(char c)
        {
            return (c >= 'a' && c <= 'z')
                   || (c >= 'A' && c <= 'Z')
                   || (c >= '0' && c <= '9')
                   || c == '_'
                   || c == '.';
        }

        private static bool IsIdentChar(char c)
        {
            return (c >= 'a' && c <= 'z')
                   || (c >= 'A' && c <= 'Z')
                   || (c >= '0' && c <= '9')
                   || c == '_';
        }

        private static bool IsValidNamespace(string ns)
        {
            if (string.IsNullOrWhiteSpace(ns))
                return false;

            if (ns[0] == '.' || ns[^1] == '.')
                return false;

            var parts = ns.Split('.');
            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p))
                    return false;

                var first = p[0];
                if (!((first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z') || first == '_'))
                    return false;

                for (int i = 1; i < p.Length; i++)
                {
                    var c = p[i];
                    if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                        return false;
                }
            }

            return true;
        }
    }
}
#nullable restore
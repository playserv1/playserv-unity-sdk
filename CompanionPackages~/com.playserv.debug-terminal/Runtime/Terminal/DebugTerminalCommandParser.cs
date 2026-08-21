using System.Collections.Generic;
using System.Text;

namespace Playserv.DebugTerminal
{
    internal static class DebugTerminalCommandParser
    {
        internal static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var normalized = input.Normalize(NormalizationForm.FormKC);
            var builder = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                if (ch == '\r' || ch == '\n' || ch == '\t')
                {
                    builder.Append(' ');
                    continue;
                }

                if (ch == '\u200B' || ch == '\u200C' || ch == '\u200D' || ch == '\uFEFF' || ch == '\u2060')
                    continue;

                if (char.IsControl(ch))
                    continue;

                builder.Append(ch);
            }

            return builder.ToString().Trim();
        }

        internal static List<string> Tokenize(string input)
        {
            var result = new List<string>();
            input = Normalize(input);
            if (string.IsNullOrWhiteSpace(input))
                return result;

            var current = new StringBuilder();
            var quote = '\0';

            foreach (var ch in input)
            {
                if (quote != '\0')
                {
                    if (ch == quote)
                        quote = '\0';
                    else
                        current.Append(ch);
                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    quote = ch;
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }

                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            return result;
        }
    }
}

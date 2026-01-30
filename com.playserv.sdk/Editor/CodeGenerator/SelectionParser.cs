#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Playserv.CodeGenerator.Editor
{
    internal static class SelectionParser
    {
        // Public AST node
        internal sealed class SelectionNode
        {
            public string Name = "";
            public string? Alias = null;
            public bool IsArray = false;

            public List<Arg> Args = new List<Arg>();
            public List<SelectionNode> Children = new List<SelectionNode>();
        }

        internal sealed class Arg
        {
            public string Name = "";
            public Value Val = new Value();
        }

        internal struct Value
        {
            public ValueKind Kind;
            public string Raw; // e.g. "20", "\"abc\"", "true", "null", "SomeEnum"
            public string? StringValue;
            public double? NumberValue;
            public bool? BoolValue;

            public override string ToString() => Raw ?? "";
        }

        internal enum ValueKind
        {
            Unknown = 0,
            String,
            Number,
            Bool,
            Null,
            Identifier
        }

        // Entry point: parse selection like "{ Id inv: Inventory(limit: 20) { ItemName } }"
        public static List<SelectionNode> Parse(string selection)
        {
            if (!TryParse(selection, out var nodes, out var error))
                throw new FormatException(error);

            return nodes;
        }

        public static bool TryParse(string selection, out List<SelectionNode> nodes, out string error)
        {
            nodes = new List<SelectionNode>();
            error = "";

            if (string.IsNullOrWhiteSpace(selection))
            {
                error = "Selection is empty.";
                return false;
            }

            var t = new Tokenizer(selection);

            if (!t.TryRead(TokenKind.LBrace, out var lb))
            {
                error = $"Expected '{{' at start. At {t.PositionInfo()}.";
                return false;
            }

            if (!TryParseFieldList(t, nodes, out error))
                return false;

            if (!t.TryRead(TokenKind.RBrace, out var rb))
            {
                error = $"Expected '}}' at end. At {t.PositionInfo()}.";
                return false;
            }

            // Allow trailing whitespace only
            t.SkipWs();
            if (!t.IsEof)
            {
                error = $"Unexpected token after selection at {t.PositionInfo()}.";
                return false;
            }

            return true;
        }

        private static bool TryParseFieldList(Tokenizer t, List<SelectionNode> outNodes, out string error)
        {
            error = "";
            while (true)
            {
                t.SkipWs();

                // End of list
                if (t.PeekKind() == TokenKind.RBrace)
                    return true;

                var node = new SelectionNode();

                if (!TryParseField(t, node, out error))
                    return false;

                outNodes.Add(node);

                t.SkipWs();
                // Optional comma between fields
                if (t.PeekKind() == TokenKind.Comma)
                    t.Read();
            }
        }

        private static bool TryParseField(Tokenizer t, SelectionNode node, out string error)
        {
            error = "";

            t.SkipWs();

            // Array form: [Inventory] ...
            bool bracketArray = false;
            if (t.PeekKind() == TokenKind.LBracket)
            {
                bracketArray = true;
                t.Read(); // [
                t.SkipWs();
            }

            // Parse "alias: Name" OR just "Name"
            // We read identifier, then if ":" follows -> it's alias, next identifier is name.
            if (!t.TryRead(TokenKind.Identifier, out var firstId))
            {
                error = $"Expected identifier at {t.PositionInfo()}.";
                return false;
            }

            t.SkipWs();

            string? alias = null;
            string name = firstId.Text;

            if (t.PeekKind() == TokenKind.Colon)
            {
                t.Read(); // :
                t.SkipWs();

                // Next identifier is the actual field name
                if (!t.TryRead(TokenKind.Identifier, out var secondId))
                {
                    error = $"Expected field name after alias ':' at {t.PositionInfo()}.";
                    return false;
                }

                alias = firstId.Text;
                name = secondId.Text;

                t.SkipWs();
            }

            node.Alias = alias;
            node.Name = name;

            // Close bracket array: [Inventory]
            if (bracketArray)
            {
                t.SkipWs();
                if (!t.TryRead(TokenKind.RBracket, out _))
                {
                    error = $"Expected ']' for array field at {t.PositionInfo()}.";
                    return false;
                }
                node.IsArray = true;
                t.SkipWs();
            }

            // Suffix array: Inventory[]
            if (!node.IsArray && t.PeekKind() == TokenKind.LBracket)
            {
                // expect "[]"
                var save = t.Cursor;
                t.Read(); // [
                t.SkipWs();
                if (t.PeekKind() == TokenKind.RBracket)
                {
                    t.Read(); // ]
                    node.IsArray = true;
                    t.SkipWs();
                }
                else
                {
                    // Not "[]", rollback (we don't support "[...]" here except at start)
                    t.Cursor = save;
                }
            }

            // Args: (a: 1, b: "x")
            if (t.PeekKind() == TokenKind.LParen)
            {
                if (!TryParseArgs(t, node.Args, out error))
                    return false;
            }

            t.SkipWs();

            // Children: { ... }
            if (t.PeekKind() == TokenKind.LBrace)
            {
                t.Read(); // {
                var kids = new List<SelectionNode>();
                if (!TryParseFieldList(t, kids, out error))
                    return false;

                if (!t.TryRead(TokenKind.RBrace, out _))
                {
                    error = $"Expected '}}' to close child selection at {t.PositionInfo()}.";
                    return false;
                }

                node.Children = kids;
            }

            return true;
        }

        private static bool TryParseArgs(Tokenizer t, List<Arg> outArgs, out string error)
        {
            error = "";
            if (!t.TryRead(TokenKind.LParen, out _))
            {
                error = $"Expected '(' at {t.PositionInfo()}.";
                return false;
            }

            while (true)
            {
                t.SkipWs();

                if (t.PeekKind() == TokenKind.RParen)
                {
                    t.Read();
                    return true;
                }

                if (!t.TryRead(TokenKind.Identifier, out var nameTok))
                {
                    error = $"Expected argument name at {t.PositionInfo()}.";
                    return false;
                }

                t.SkipWs();

                if (!t.TryRead(TokenKind.Colon, out _))
                {
                    error = $"Expected ':' after argument name at {t.PositionInfo()}.";
                    return false;
                }

                t.SkipWs();

                if (!TryParseValue(t, out var val, out error))
                    return false;

                outArgs.Add(new Arg { Name = nameTok.Text, Val = val });

                t.SkipWs();
                if (t.PeekKind() == TokenKind.Comma)
                {
                    t.Read();
                    continue;
                }

                t.SkipWs();
                if (t.PeekKind() == TokenKind.RParen)
                {
                    t.Read();
                    return true;
                }

                error = $"Expected ',' or ')' in args at {t.PositionInfo()}.";
                return false;
            }
        }

        private static bool TryParseValue(Tokenizer t, out Value v, out string error)
        {
            error = "";
            v = new Value { Kind = ValueKind.Unknown, Raw = "" };

            var k = t.PeekKind();
            if (k == TokenKind.String)
            {
                var tok = t.Read();
                v.Kind = ValueKind.String;
                v.Raw = tok.TextRaw; // includes quotes
                v.StringValue = tok.Text; // unescaped
                return true;
            }

            if (k == TokenKind.Number)
            {
                var tok = t.Read();
                v.Kind = ValueKind.Number;
                v.Raw = tok.Text;
                if (double.TryParse(tok.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    v.NumberValue = d;
                return true;
            }

            if (k == TokenKind.Identifier)
            {
                var tok = t.Read();
                var s = tok.Text;

                if (s.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    v.Kind = ValueKind.Bool;
                    v.Raw = s;
                    v.BoolValue = true;
                    return true;
                }
                if (s.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    v.Kind = ValueKind.Bool;
                    v.Raw = s;
                    v.BoolValue = false;
                    return true;
                }
                if (s.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    v.Kind = ValueKind.Null;
                    v.Raw = s;
                    return true;
                }

                // Enum-like or identifier-like value
                v.Kind = ValueKind.Identifier;
                v.Raw = s;
                v.StringValue = s;
                return true;
            }

            error = $"Expected value (string/number/bool/null/identifier) at {t.PositionInfo()}.";
            return false;
        }

        // ---------------- Tokenizer ----------------

        private enum TokenKind
        {
            Eof = 0,
            Identifier,
            Number,
            String,

            LBrace,    // {
            RBrace,    // }
            LParen,    // (
            RParen,    // )
            LBracket,  // [
            RBracket,  // ]
            Colon,     // :
            Comma      // ,
        }

        private struct Token
        {
            public TokenKind Kind;
            public string Text;     // cooked (for string: unescaped; for others: raw)
            public string TextRaw;  // raw (for string: includes quotes; else same as Text)
            public int Pos;
        }

        private sealed class Tokenizer
        {
            private readonly string _s;

            public int Cursor;
            public bool IsEof => Cursor >= _s.Length;

            public Tokenizer(string s)
            {
                _s = s ?? "";
                Cursor = 0;
            }

            public int Line
            {
                get
                {
                    int line = 1;
                    for (int i = 0; i < Cursor && i < _s.Length; i++)
                        if (_s[i] == '\n') line++;
                    return line;
                }
            }

            public int Col
            {
                get
                {
                    int col = 1;
                    for (int i = Cursor - 1; i >= 0 && i < _s.Length; i--)
                    {
                        if (_s[i] == '\n') break;
                        col++;
                    }
                    return col;
                }
            }

            public string PositionInfo() => $"line {Line}, col {Col}";

            public void SkipWs()
            {
                while (!IsEof)
                {
                    var c = _s[Cursor];
                    if (char.IsWhiteSpace(c))
                    {
                        Cursor++;
                        continue;
                    }
                    break;
                }
            }

            public TokenKind PeekKind()
            {
                SkipWs();
                if (IsEof) return TokenKind.Eof;

                char c = _s[Cursor];
                switch (c)
                {
                    case '{': return TokenKind.LBrace;
                    case '}': return TokenKind.RBrace;
                    case '(': return TokenKind.LParen;
                    case ')': return TokenKind.RParen;
                    case '[': return TokenKind.LBracket;
                    case ']': return TokenKind.RBracket;
                    case ':': return TokenKind.Colon;
                    case ',': return TokenKind.Comma;
                    case '"': return TokenKind.String;
                }

                if (IsIdentStart(c)) return TokenKind.Identifier;
                if (IsNumberStart(c)) return TokenKind.Number;

                // Unknown char -> treat as EOF-like error; caller will fail on TryRead
                return TokenKind.Eof;
            }

            public bool TryRead(TokenKind kind, out Token tok)
            {
                tok = default;
                SkipWs();
                if (PeekKind() != kind)
                    return false;

                tok = Read();
                return tok.Kind == kind;
            }

            public Token Read()
            {
                SkipWs();
                if (IsEof) return new Token { Kind = TokenKind.Eof, Text = "", TextRaw = "", Pos = Cursor };

                char c = _s[Cursor];
                int pos = Cursor;

                // Single-char tokens
                switch (c)
                {
                    case '{': Cursor++; return new Token { Kind = TokenKind.LBrace, Text = "{", TextRaw = "{", Pos = pos };
                    case '}': Cursor++; return new Token { Kind = TokenKind.RBrace, Text = "}", TextRaw = "}", Pos = pos };
                    case '(': Cursor++; return new Token { Kind = TokenKind.LParen, Text = "(", TextRaw = "(", Pos = pos };
                    case ')': Cursor++; return new Token { Kind = TokenKind.RParen, Text = ")", TextRaw = ")", Pos = pos };
                    case '[': Cursor++; return new Token { Kind = TokenKind.LBracket, Text = "[", TextRaw = "[", Pos = pos };
                    case ']': Cursor++; return new Token { Kind = TokenKind.RBracket, Text = "]", TextRaw = "]", Pos = pos };
                    case ':': Cursor++; return new Token { Kind = TokenKind.Colon, Text = ":", TextRaw = ":", Pos = pos };
                    case ',': Cursor++; return new Token { Kind = TokenKind.Comma, Text = ",", TextRaw = ",", Pos = pos };
                    case '"':
                        return ReadString();
                }

                if (IsIdentStart(c))
                    return ReadIdentifier();

                if (IsNumberStart(c))
                    return ReadNumber();

                // Unknown char
                Cursor++;
                return new Token { Kind = TokenKind.Eof, Text = "", TextRaw = "", Pos = pos };
            }

            private Token ReadIdentifier()
            {
                int pos = Cursor;
                var sb = new StringBuilder();
                while (!IsEof)
                {
                    char c = _s[Cursor];
                    if (IsIdentPart(c))
                    {
                        sb.Append(c);
                        Cursor++;
                        continue;
                    }
                    break;
                }

                var raw = sb.ToString();
                return new Token { Kind = TokenKind.Identifier, Text = raw, TextRaw = raw, Pos = pos };
            }

            private Token ReadNumber()
            {
                int pos = Cursor;
                var sb = new StringBuilder();

                // Allow leading '-'
                if (!IsEof && _s[Cursor] == '-')
                {
                    sb.Append('-');
                    Cursor++;
                }

                while (!IsEof)
                {
                    char c = _s[Cursor];
                    if (char.IsDigit(c) || c == '.')
                    {
                        sb.Append(c);
                        Cursor++;
                        continue;
                    }
                    break;
                }

                var raw = sb.ToString();
                return new Token { Kind = TokenKind.Number, Text = raw, TextRaw = raw, Pos = pos };
            }

            private Token ReadString()
            {
                int pos = Cursor;
                var raw = new StringBuilder();
                var cooked = new StringBuilder();

                // Opening quote
                raw.Append('"');
                Cursor++;

                while (!IsEof)
                {
                    char c = _s[Cursor];
                    Cursor++;

                    raw.Append(c);

                    if (c == '"')
                        break;

                    if (c == '\\' && !IsEof)
                    {
                        char esc = _s[Cursor];
                        Cursor++;
                        raw.Append(esc);

                        // Minimal unescape
                        switch (esc)
                        {
                            case '"': cooked.Append('"'); break;
                            case '\\': cooked.Append('\\'); break;
                            case 'n': cooked.Append('\n'); break;
                            case 'r': cooked.Append('\r'); break;
                            case 't': cooked.Append('\t'); break;
                            default: cooked.Append(esc); break;
                        }
                        continue;
                    }

                    cooked.Append(c);
                }

                var rawText = raw.ToString();
                var cookedText = cooked.ToString();

                return new Token { Kind = TokenKind.String, Text = cookedText, TextRaw = rawText, Pos = pos };
            }

            private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
            private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

            private static bool IsNumberStart(char c) => char.IsDigit(c) || c == '-' ;
        }
    }
}
#nullable restore
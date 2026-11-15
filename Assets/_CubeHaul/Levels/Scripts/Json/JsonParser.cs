using System;
using System.Globalization;
using System.Text;

namespace CubeHaul.Levels.Json
{
    public sealed class JsonParseException : Exception
    {
        public int Line { get; }
        public int Column { get; }

        public JsonParseException(string message, int line, int column)
            : base($"{message} at line {line}, column {column}")
        {
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// A strict RFC 8259 JSON parser with no dependencies, so the data layer runs the same in Unity (Mono or IL2CPP)
    /// and in plain .NET. It accepts exactly what JavaScript's JSON.parse accepts: no comments, no trailing commas,
    /// no single quotes, no NaN or Infinity literals. Numbers too large for a double become infinity, as in
    /// JavaScript. Parsing is culture-independent.
    /// </summary>
    public sealed class JsonParser
    {
        public const int MaxDepth = 128;

        readonly string text;
        int pos;
        int depth;

        JsonParser(string text)
        {
            this.text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public static JsonValue Parse(string text)
        {
            var p = new JsonParser(text);
            p.SkipWhitespace();
            JsonValue value = p.ParseValue();
            p.SkipWhitespace();
            if (p.pos < p.text.Length) throw p.Error("unexpected text after the JSON value");
            return value;
        }

        JsonValue ParseValue()
        {
            if (pos >= text.Length) throw Error("unexpected end of input");
            char c = text[pos];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return JsonValue.FromString(ParseString());
                case 't': Expect("true"); return JsonValue.FromBoolean(true);
                case 'f': Expect("false"); return JsonValue.FromBoolean(false);
                case 'n': Expect("null"); return JsonValue.Null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return JsonValue.FromNumber(ParseNumber());
                    throw Error($"unexpected character '{c}'");
            }
        }

        JsonValue ParseObject()
        {
            Enter();
            pos++; // {
            var obj = JsonValue.NewObject();
            SkipWhitespace();
            if (Peek() == '}')
            {
                pos++;
                depth--;
                return obj;
            }
            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"') throw Error("expected a string key");
                string key = ParseString();
                SkipWhitespace();
                if (Peek() != ':') throw Error("expected ':'");
                pos++;
                SkipWhitespace();
                obj.Set(key, ParseValue());
                SkipWhitespace();
                char c = Peek();
                pos++;
                if (c == ',') continue;
                if (c == '}') break;
                pos--;
                throw Error("expected ',' or '}'");
            }
            depth--;
            return obj;
        }

        JsonValue ParseArray()
        {
            Enter();
            pos++; // [
            var arr = JsonValue.NewArray();
            SkipWhitespace();
            if (Peek() == ']')
            {
                pos++;
                depth--;
                return arr;
            }
            while (true)
            {
                SkipWhitespace();
                arr.Add(ParseValue());
                SkipWhitespace();
                char c = Peek();
                pos++;
                if (c == ',') continue;
                if (c == ']') break;
                pos--;
                throw Error("expected ',' or ']'");
            }
            depth--;
            return arr;
        }

        string ParseString()
        {
            pos++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= text.Length) throw Error("unterminated string");
                char c = text[pos++];
                if (c == '"') return sb.ToString();
                if (c < 0x20) throw Error("control character in string");
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (pos >= text.Length) throw Error("unterminated escape");
                char e = text[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 > text.Length) throw Error("bad \\u escape");
                        int code = 0;
                        for (int k = 0; k < 4; k++)
                        {
                            int d = HexDigit(text[pos + k]);
                            if (d < 0) throw Error("bad \\u escape");
                            code = code * 16 + d;
                        }
                        pos += 4;
                        sb.Append((char)code);
                        break;
                    default:
                        pos--;
                        throw Error($"bad escape '\\{e}'");
                }
            }
        }

        double ParseNumber()
        {
            int start = pos;
            if (Peek() == '-') pos++;
            if (Peek() == '0') pos++;
            else if (IsDigit(Peek())) while (IsDigit(Peek())) pos++;
            else throw Error("bad number");
            if (Peek() == '.')
            {
                pos++;
                if (!IsDigit(Peek())) throw Error("bad number");
                while (IsDigit(Peek())) pos++;
            }
            if (Peek() == 'e' || Peek() == 'E')
            {
                pos++;
                if (Peek() == '+' || Peek() == '-') pos++;
                if (!IsDigit(Peek())) throw Error("bad number");
                while (IsDigit(Peek())) pos++;
            }
            string raw = text.Substring(start, pos - start);
            try
            {
                return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                // Older runtimes (Mono) throw where .NET Core and JavaScript return infinity.
                return raw[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            }
        }

        void Expect(string word)
        {
            if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0) throw Error("unexpected literal");
            pos += word.Length;
        }

        void Enter()
        {
            if (++depth > MaxDepth) throw Error($"nested deeper than {MaxDepth}");
        }

        char Peek() => pos < text.Length ? text[pos] : '\0';

        void SkipWhitespace()
        {
            while (pos < text.Length)
            {
                char c = text[pos];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') pos++;
                else break;
            }
        }

        static bool IsDigit(char c) => c >= '0' && c <= '9';

        static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        JsonParseException Error(string message)
        {
            int line = 1;
            int column = 1;
            for (int i = 0; i < pos && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else column++;
            }
            return new JsonParseException(message, line, column);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static class MiniJSON
{
    public static object Deserialize(string json)
    {
        if (json == null) return null;
        return Parser.Parse(json);
    }

    private sealed class Parser : IDisposable
    {
        private const string WORD_BREAK = "{}[],:\"";
        private readonly StringReader _json;

        private Parser(string jsonString)
        {
            _json = new StringReader(jsonString);
        }

        public static object Parse(string jsonString)
        {
            using var instance = new Parser(jsonString);
            return instance.ParseValue();
        }

        public void Dispose() { }

        private Dictionary<string, object> ParseObject()
        {
            var table = new Dictionary<string, object>();
            _json.Read(); // {

            while (true)
            {
                switch (NextToken)
                {
                    case TOKEN.NONE:
                        return null;

                    case TOKEN.CURLY_CLOSE:
                        _json.Read();
                        return table;

                    default:
                        string name = ParseString();
                        if (name == null) return null;

                        if (NextToken != TOKEN.COLON) return null;
                        _json.Read(); // :

                        table[name] = ParseValue();
                        break;
                }

                switch (NextToken)
                {
                    case TOKEN.COMMA:
                        _json.Read();
                        continue;

                    case TOKEN.CURLY_CLOSE:
                        _json.Read();
                        return table;

                    default:
                        return null;
                }
            }
        }

        private List<object> ParseArray()
        {
            var array = new List<object>();
            _json.Read(); // [

            while (true)
            {
                TOKEN nextToken = NextToken;

                switch (nextToken)
                {
                    case TOKEN.NONE:
                        return null;

                    case TOKEN.SQUARE_CLOSE:
                        _json.Read();
                        return array;

                    case TOKEN.COMMA:
                        _json.Read();
                        break;

                    default:
                        array.Add(ParseValue());
                        break;
                }
            }
        }

        private object ParseValue()
        {
            switch (NextToken)
            {
                case TOKEN.STRING:
                    return ParseString();

                case TOKEN.NUMBER:
                    return ParseNumber();

                case TOKEN.CURLY_OPEN:
                    return ParseObject();

                case TOKEN.SQUARE_OPEN:
                    return ParseArray();

                case TOKEN.TRUE:
                    return true;

                case TOKEN.FALSE:
                    return false;

                case TOKEN.NULL:
                    return null;

                default:
                    return null;
            }
        }

        private string ParseString()
        {
            var sb = new StringBuilder();
            _json.Read(); // "

            bool parsing = true;
            while (parsing)
            {
                if (_json.Peek() == -1) break;

                char c = NextChar;
                switch (c)
                {
                    case '"':
                        parsing = false;
                        break;

                    case '\\':
                        if (_json.Peek() == -1) parsing = false;
                        else
                        {
                            c = NextChar;
                            switch (c)
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
                                    var hex = new char[4];
                                    for (int i = 0; i < 4; i++) hex[i] = NextChar;
                                    sb.Append((char)Convert.ToInt32(new string(hex), 16));
                                    break;
                            }
                        }
                        break;

                    default:
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        private object ParseNumber()
        {
            string number = NextWord;

            if (number.IndexOf('.') != -1 || number.IndexOf('e') != -1 || number.IndexOf('E') != -1)
            {
                if (double.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                    return d;
                return 0d;
            }

            if (long.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out long l))
                return l;

            return 0L;
        }

        private void EatWhitespace()
        {
            while (_json.Peek() != -1 && char.IsWhiteSpace(PeekChar))
                _json.Read();
        }

        private char PeekChar => Convert.ToChar(_json.Peek());
        private char NextChar => Convert.ToChar(_json.Read());

        private string NextWord
        {
            get
            {
                var sb = new StringBuilder();
                while (_json.Peek() != -1 && !IsWordBreak(PeekChar))
                    sb.Append(NextChar);
                return sb.ToString();
            }
        }

        private TOKEN NextToken
        {
            get
            {
                EatWhitespace();
                if (_json.Peek() == -1) return TOKEN.NONE;

                char c = PeekChar;
                switch (c)
                {
                    case '{': return TOKEN.CURLY_OPEN;
                    case '}': return TOKEN.CURLY_CLOSE;
                    case '[': return TOKEN.SQUARE_OPEN;
                    case ']': return TOKEN.SQUARE_CLOSE;
                    case ',': return TOKEN.COMMA;
                    case '"': return TOKEN.STRING;
                    case ':': return TOKEN.COLON;
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                    case '-':
                        return TOKEN.NUMBER;
                }

                string word = NextWord;
                switch (word)
                {
                    case "false": return TOKEN.FALSE;
                    case "true": return TOKEN.TRUE;
                    case "null": return TOKEN.NULL;
                }

                return TOKEN.NONE;
            }
        }

        private static bool IsWordBreak(char c) => char.IsWhiteSpace(c) || WORD_BREAK.IndexOf(c) != -1;

        private enum TOKEN
        {
            NONE, CURLY_OPEN, CURLY_CLOSE, SQUARE_OPEN, SQUARE_CLOSE, COLON, COMMA,
            STRING, NUMBER, TRUE, FALSE, NULL
        }

        private sealed class StringReader
        {
            private readonly string _s;
            private int _pos;

            public StringReader(string s) { _s = s; _pos = 0; }

            public int Peek()
            {
                if (_pos >= _s.Length) return -1;
                return _s[_pos];
            }

            public int Read()
            {
                if (_pos >= _s.Length) return -1;
                return _s[_pos++];
            }
        }
    }
}
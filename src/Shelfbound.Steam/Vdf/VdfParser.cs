using System.Text;

namespace Shelfbound.Steam.Vdf;

/// <summary>
/// Minimal parser for Valve's text VDF / KeyValues format used by libraryfolders.vdf,
/// appmanifest_*.acf, loginusers.vdf and friends. Handles quoted tokens with escapes,
/// nested braces, and // line comments. It does not evaluate platform conditionals
/// (e.g. [$WIN32]) or #include directives, which do not appear in the files Shelfbound reads.
/// </summary>
public static class VdfParser
{
    public static VdfObject Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > SteamInputLimits.MaxVdfTextChars)
            throw new FormatException($"VDF input exceeds the {SteamInputLimits.MaxVdfTextChars}-character limit.");

        var lexer = new Lexer(text);
        var root = new VdfObject();
        while (true)
        {
            var token = lexer.Next();
            if (token.Type == TokenType.Eof)
                break;
            if (token.Type != TokenType.String)
                throw new FormatException($"Unexpected token '{token.Type}' at top level (position {token.Position}).");

            ReadKeyValue(lexer, root, token.Text, depth: 0);
        }
        return root;
    }

    public static VdfObject ParseFile(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > SteamInputLimits.MaxVdfFileBytes)
            throw new FormatException($"VDF file exceeds the {SteamInputLimits.MaxVdfFileBytes}-byte limit.");
        return Parse(File.ReadAllText(path));
    }

    private static void ReadKeyValue(Lexer lexer, VdfObject target, string key, int depth)
    {
        var next = lexer.Next();
        switch (next.Type)
        {
            case TokenType.OpenBrace:
                int childDepth = checked(depth + 1);
                if (childDepth > SteamInputLimits.MaxVdfDepth)
                    throw new FormatException($"VDF nesting exceeds the depth limit of {SteamInputLimits.MaxVdfDepth}.");
                target.SetObject(key, ReadObject(lexer, childDepth));
                break;
            case TokenType.String:
                target.SetValue(key, next.Text);
                break;
            default:
                throw new FormatException($"Expected a value or '{{' after key '{key}' (position {next.Position}).");
        }
    }

    private static VdfObject ReadObject(Lexer lexer, int depth)
    {
        var obj = new VdfObject();
        while (true)
        {
            var token = lexer.Next();
            if (token.Type == TokenType.CloseBrace)
                return obj;
            if (token.Type == TokenType.Eof)
                throw new FormatException("Unexpected end of input inside object (missing '}').");
            if (token.Type != TokenType.String)
                throw new FormatException($"Expected a key or '}}' (position {token.Position}).");

            ReadKeyValue(lexer, obj, token.Text, depth);
        }
    }

    private enum TokenType { String, OpenBrace, CloseBrace, Eof }

    private readonly record struct Token(TokenType Type, string Text, int Position);

    private sealed class Lexer(string source)
    {
        private readonly string _s = source;
        private int _i;

        public Token Next()
        {
            SkipTrivia();
            if (_i >= _s.Length)
                return new Token(TokenType.Eof, string.Empty, _i);

            char c = _s[_i];
            return c switch
            {
                '{' => new Token(TokenType.OpenBrace, "{", _i++),
                '}' => new Token(TokenType.CloseBrace, "}", _i++),
                '"' => ReadQuoted(),
                _ => ReadUnquoted(),
            };
        }

        private void SkipTrivia()
        {
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (char.IsWhiteSpace(c))
                {
                    _i++;
                }
                else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                {
                    while (_i < _s.Length && _s[_i] != '\n')
                        _i++;
                }
                else
                {
                    break;
                }
            }
        }

        private Token ReadQuoted()
        {
            int start = _i;
            _i++; // consume opening quote
            var sb = new StringBuilder();
            while (_i < _s.Length)
            {
                char c = _s[_i++];
                if (c == '"')
                    return new Token(TokenType.String, sb.ToString(), start);

                if (c == '\\' && _i < _s.Length)
                {
                    char e = _s[_i++];
                    sb.Append(e switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '\\' => '\\',
                        '"' => '"',
                        _ => e,
                    });
                }
                else
                {
                    sb.Append(c);
                }
            }
            throw new FormatException($"Unterminated quoted string starting at position {start}.");
        }

        private Token ReadUnquoted()
        {
            int start = _i;
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (char.IsWhiteSpace(c) || c is '{' or '}' or '"')
                    break;
                _i++;
            }
            return new Token(TokenType.String, _s[start.._i], start);
        }
    }
}

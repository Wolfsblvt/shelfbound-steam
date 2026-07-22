using System.Text;

namespace Shelfbound.Steam.Vdf;

/// <summary>A scalar selected from one VDF object path without retaining unrelated values.</summary>
internal sealed record VdfScalarSelection(string? Value, bool HasMatchingSibling);

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

        return Parse(new Lexer(text));
    }

    public static VdfObject ParseFile(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > SteamInputLimits.MaxVdfFileBytes)
            throw new FormatException($"VDF file exceeds the {SteamInputLimits.MaxVdfFileBytes}-byte limit.");
        return Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// Selects one scalar at an object path while discarding unrelated scalar contents as they are read.
    /// The sibling flag considers only direct scalar keys and excludes the selected key itself.
    /// </summary>
    internal static VdfScalarSelection SelectValue(
        string text,
        IReadOnlyList<string> objectPath,
        string valueKey,
        string siblingPrefix)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > SteamInputLimits.MaxVdfTextChars)
            throw new FormatException($"VDF input exceeds the {SteamInputLimits.MaxVdfTextChars}-character limit.");

        return SelectValue(new Lexer(text), objectPath, valueKey, siblingPrefix);
    }

    /// <inheritdoc cref="SelectValue(string, IReadOnlyList{string}, string, string)"/>
    internal static VdfScalarSelection SelectFileValue(
        string path,
        IReadOnlyList<string> objectPath,
        string valueKey,
        string siblingPrefix)
    {
        using StreamReader reader = OpenBoundedFile(path);
        return SelectValue(new StreamingLexer(reader), objectPath, valueKey, siblingPrefix);
    }

    private static StreamReader OpenBoundedFile(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            if (stream.Length > SteamInputLimits.MaxVdfFileBytes)
                throw new FormatException($"VDF file exceeds the {SteamInputLimits.MaxVdfFileBytes}-byte limit.");
            return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static VdfObject Parse(ITokenReader lexer)
    {
        var root = new VdfObject();
        while (true)
        {
            Token token = lexer.Next();
            if (token.Type == TokenType.Eof)
                return root;
            if (token.Type != TokenType.String)
                throw new FormatException($"Unexpected token '{token.Type}' at top level (position {token.Position}).");

            ReadKeyValue(lexer, root, token.Text, depth: 0);
        }
    }

    private static VdfScalarSelection SelectValue(
        ITokenReader lexer,
        IReadOnlyList<string> objectPath,
        string valueKey,
        string siblingPrefix)
    {
        ArgumentNullException.ThrowIfNull(objectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(siblingPrefix);
        if (objectPath.Count == 0 || objectPath.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("The selected VDF object path must contain only non-empty segments.", nameof(objectPath));

        VdfScalarSelection? selection = null;
        while (true)
        {
            Token token = lexer.Next();
            if (token.Type == TokenType.Eof)
                return selection ?? new VdfScalarSelection(null, false);
            if (token.Type != TokenType.String)
                throw new FormatException($"Unexpected token '{token.Type}' at top level (position {token.Position}).");

            VdfScalarSelection? candidate = ReadSelectedKeyValue(
                lexer,
                token.Text,
                depth: 0,
                pathIndex: 0,
                objectPath,
                valueKey,
                siblingPrefix,
                targetSelection: null);
            if (candidate is not null)
                selection = candidate;
        }
    }

    private static void ReadKeyValue(ITokenReader lexer, VdfObject target, string key, int depth)
    {
        Token next = lexer.Next();
        switch (next.Type)
        {
            case TokenType.OpenBrace:
                int childDepth = GetChildDepth(depth);
                target.SetObject(key, ReadObject(lexer, childDepth));
                break;
            case TokenType.String:
                target.SetValue(key, next.Text);
                break;
            default:
                throw new FormatException($"Expected a value or '{{' after key '{key}' (position {next.Position}).");
        }
    }

    private static VdfObject ReadObject(ITokenReader lexer, int depth)
    {
        var obj = new VdfObject();
        while (true)
        {
            Token token = lexer.Next();
            if (token.Type == TokenType.CloseBrace)
                return obj;
            if (token.Type == TokenType.Eof)
                throw new FormatException("Unexpected end of input inside object (missing '}').");
            if (token.Type != TokenType.String)
                throw new FormatException($"Expected a key or '}}' (position {token.Position}).");

            ReadKeyValue(lexer, obj, token.Text, depth);
        }
    }

    private static VdfScalarSelection? ReadSelectedKeyValue(
        ITokenReader lexer,
        string key,
        int depth,
        int pathIndex,
        IReadOnlyList<string> objectPath,
        string valueKey,
        string siblingPrefix,
        MutableSelection? targetSelection)
    {
        bool inTargetObject = pathIndex == objectPath.Count;
        bool isSelectedKey = inTargetObject && key.Equals(valueKey, StringComparison.OrdinalIgnoreCase);
        bool isMatchingSibling = inTargetObject &&
            !isSelectedKey &&
            key.StartsWith(siblingPrefix, StringComparison.OrdinalIgnoreCase);
        Token next = lexer.Next(captureText: isSelectedKey);

        switch (next.Type)
        {
            case TokenType.OpenBrace:
                int childDepth = GetChildDepth(depth);
                if (!inTargetObject && key.Equals(objectPath[pathIndex], StringComparison.OrdinalIgnoreCase))
                {
                    return ReadSelectedObject(
                        lexer,
                        childDepth,
                        pathIndex + 1,
                        objectPath,
                        valueKey,
                        siblingPrefix);
                }

                SkipObject(lexer, childDepth);
                return null;
            case TokenType.String:
                if (isSelectedKey)
                    targetSelection!.Value = next.Text;
                else if (isMatchingSibling)
                    targetSelection!.HasMatchingSibling = true;
                return null;
            default:
                throw new FormatException($"Expected a value or '{{' after a key (position {next.Position}).");
        }
    }

    private static VdfScalarSelection ReadSelectedObject(
        ITokenReader lexer,
        int depth,
        int pathIndex,
        IReadOnlyList<string> objectPath,
        string valueKey,
        string siblingPrefix)
    {
        var targetSelection = pathIndex == objectPath.Count ? new MutableSelection() : null;
        VdfScalarSelection? nestedSelection = null;

        while (true)
        {
            Token token = lexer.Next();
            if (token.Type == TokenType.CloseBrace)
            {
                if (targetSelection is not null)
                    return new VdfScalarSelection(targetSelection.Value, targetSelection.HasMatchingSibling);
                return nestedSelection ?? new VdfScalarSelection(null, false);
            }
            if (token.Type == TokenType.Eof)
                throw new FormatException("Unexpected end of input inside object (missing '}').");
            if (token.Type != TokenType.String)
                throw new FormatException($"Expected a key or '}}' (position {token.Position}).");

            // Direct keys must be visible so path components, the exact target, and the account-mismatch
            // prefix can be recognized. Values and every unrelated subtree remain uncaptured.
            VdfScalarSelection? candidate = ReadSelectedKeyValue(
                lexer,
                token.Text,
                depth,
                pathIndex,
                objectPath,
                valueKey,
                siblingPrefix,
                targetSelection);
            if (candidate is not null)
                nestedSelection = candidate;
        }
    }

    private static void SkipObject(ITokenReader lexer, int depth)
    {
        while (true)
        {
            Token key = lexer.Next(captureText: false);
            if (key.Type == TokenType.CloseBrace)
                return;
            if (key.Type == TokenType.Eof)
                throw new FormatException("Unexpected end of input inside object (missing '}').");
            if (key.Type != TokenType.String)
                throw new FormatException($"Expected a key or '}}' (position {key.Position}).");

            Token value = lexer.Next(captureText: false);
            if (value.Type == TokenType.OpenBrace)
                SkipObject(lexer, GetChildDepth(depth));
            else if (value.Type != TokenType.String)
                throw new FormatException($"Expected a value or '{{' after a key (position {value.Position}).");
        }
    }

    private static int GetChildDepth(int depth)
    {
        int childDepth = checked(depth + 1);
        if (childDepth > SteamInputLimits.MaxVdfDepth)
            throw new FormatException($"VDF nesting exceeds the depth limit of {SteamInputLimits.MaxVdfDepth}.");
        return childDepth;
    }

    private sealed class MutableSelection
    {
        public string? Value { get; set; }
        public bool HasMatchingSibling { get; set; }
    }

    private enum TokenType { String, OpenBrace, CloseBrace, Eof }

    private readonly record struct Token(TokenType Type, string Text, int Position);

    private interface ITokenReader
    {
        Token Next(bool captureText = true);
    }

    private sealed class Lexer(string source) : ITokenReader
    {
        private readonly string _source = source;
        private int _position;

        public Token Next(bool captureText = true)
        {
            SkipTrivia();
            if (_position >= _source.Length)
                return new Token(TokenType.Eof, string.Empty, _position);

            char c = _source[_position];
            return c switch
            {
                '{' => new Token(TokenType.OpenBrace, "{", _position++),
                '}' => new Token(TokenType.CloseBrace, "}", _position++),
                '"' => ReadQuoted(captureText),
                _ => ReadUnquoted(captureText),
            };
        }

        private void SkipTrivia()
        {
            while (_position < _source.Length)
            {
                char c = _source[_position];
                if (char.IsWhiteSpace(c))
                {
                    _position++;
                }
                else if (c == '/' && _position + 1 < _source.Length && _source[_position + 1] == '/')
                {
                    while (_position < _source.Length && _source[_position] != '\n')
                        _position++;
                }
                else
                {
                    break;
                }
            }
        }

        private Token ReadQuoted(bool captureText)
        {
            int start = _position;
            _position++; // consume opening quote
            StringBuilder? builder = captureText ? new StringBuilder() : null;
            while (_position < _source.Length)
            {
                char c = _source[_position++];
                if (c == '"')
                    return new Token(TokenType.String, builder?.ToString() ?? string.Empty, start);

                if (c == '\\' && _position < _source.Length)
                {
                    char escaped = _source[_position++];
                    if (builder is not null)
                    {
                        builder.Append(escaped switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            'r' => '\r',
                            '\\' => '\\',
                            '"' => '"',
                            _ => escaped,
                        });
                    }
                }
                else
                {
                    builder?.Append(c);
                }
            }
            throw new FormatException($"Unterminated quoted string starting at position {start}.");
        }

        private Token ReadUnquoted(bool captureText)
        {
            int start = _position;
            while (_position < _source.Length)
            {
                char c = _source[_position];
                if (char.IsWhiteSpace(c) || c is '{' or '}' or '"')
                    break;
                _position++;
            }
            return new Token(
                TokenType.String,
                captureText ? _source[start.._position] : string.Empty,
                start);
        }
    }

    private sealed class StreamingLexer(TextReader reader) : ITokenReader
    {
        private readonly TextReader _reader = reader;
        private readonly List<int> _lookahead = [];
        private int _position;

        public Token Next(bool captureText = true)
        {
            SkipTrivia();
            int value = Peek();
            if (value < 0)
                return new Token(TokenType.Eof, string.Empty, _position);

            char c = (char)value;
            Token token = c switch
            {
                '{' => new Token(TokenType.OpenBrace, "{", ConsumePosition()),
                '}' => new Token(TokenType.CloseBrace, "}", ConsumePosition()),
                '"' => ReadQuoted(captureText),
                _ => ReadUnquoted(captureText),
            };
            return token;
        }

        private void SkipTrivia()
        {
            while (true)
            {
                int value = Peek();
                if (value < 0)
                    return;

                char c = (char)value;
                if (char.IsWhiteSpace(c))
                {
                    ReadChar();
                }
                else if (c == '/' && Peek(1) == '/')
                {
                    while (Peek() is >= 0 and not '\n')
                        ReadChar();
                }
                else
                {
                    return;
                }
            }
        }

        private Token ReadQuoted(bool captureText)
        {
            int start = _position;
            ReadChar(); // consume opening quote
            StringBuilder? builder = captureText ? new StringBuilder() : null;
            while (Peek() >= 0)
            {
                char c = ReadChar();
                if (c == '"')
                    return new Token(TokenType.String, builder?.ToString() ?? string.Empty, start);

                if (c == '\\' && Peek() >= 0)
                {
                    char escaped = ReadChar();
                    if (builder is not null)
                    {
                        builder.Append(escaped switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            'r' => '\r',
                            '\\' => '\\',
                            '"' => '"',
                            _ => escaped,
                        });
                    }
                }
                else
                {
                    builder?.Append(c);
                }
            }
            throw new FormatException($"Unterminated quoted string starting at position {start}.");
        }

        private Token ReadUnquoted(bool captureText)
        {
            int start = _position;
            StringBuilder? builder = captureText ? new StringBuilder() : null;
            while (Peek() >= 0)
            {
                char c = (char)Peek();
                if (char.IsWhiteSpace(c) || c is '{' or '}' or '"')
                    break;
                char consumed = ReadChar();
                builder?.Append(consumed);
            }
            return new Token(TokenType.String, builder?.ToString() ?? string.Empty, start);
        }

        private int ConsumePosition()
        {
            int position = _position;
            ReadChar();
            return position;
        }

        private int Peek(int offset = 0)
        {
            while (_lookahead.Count <= offset)
            {
                int value = _reader.Read();
                _lookahead.Add(value);
                if (value < 0)
                    break;
            }
            return offset < _lookahead.Count ? _lookahead[offset] : -1;
        }

        private char ReadChar()
        {
            int value = Peek();
            if (value < 0)
                throw new InvalidOperationException("Cannot read past the end of the VDF input.");
            if (_position >= SteamInputLimits.MaxVdfTextChars)
                throw new FormatException($"VDF input exceeds the {SteamInputLimits.MaxVdfTextChars}-character limit.");

            _lookahead.RemoveAt(0);
            _position++;
            return (char)value;
        }
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace YoutubeDl.Engine.Discovery;

/// <summary>Reads bounded data literals only. Calls, identifiers, operators and script execution are rejected.</summary>
internal sealed class JavaScriptDataLiteral(string text, CancellationToken ct, bool allowIdentifierKeys = false)
{
    private int _position;
    private int _nodes;
    internal static JsonObject ObjectArgument(string text, int start, out int end, CancellationToken ct)
    {
        return ObjectArgument(text, start, out end, ct, allowIdentifierKeys: false);
    }
    internal static JsonObject ObjectArgument(string text, int start, out int end, CancellationToken ct, bool allowIdentifierKeys)
    {
        if (text.Length > 8 * 1024 * 1024 || start < 0 || start >= text.Length) throw new InvalidDataException("The literal data exceeds its supported bounds.");
        var reader = new JavaScriptDataLiteral(text, ct, allowIdentifierKeys) { _position = start }; var result = reader.Value(0) as JsonObject ?? throw new InvalidDataException("A literal object is required.");
        end = reader._position; return result;
    }
    private JsonNode? Value(int depth)
    {
        ct.ThrowIfCancellationRequested(); if (depth > 64 || ++_nodes > 10000) throw new InvalidDataException("The literal data exceeds its nesting or field limits.");
        White(); if (_position >= text.Length) throw new InvalidDataException("The literal data is truncated.");
        var current = text[_position];
        if (current is '\'' or '"') return JsonValue.Create(String());
        if (current == '{')
        {
            _position++; var result = new JsonObject(); White(); if (Take('}')) return result;
            do { White(); if (_position >= text.Length) throw new InvalidDataException("The literal object is truncated.");
                var key = text[_position] is '\'' or '"' ? String() : allowIdentifierKeys ? Identifier() : throw new InvalidDataException("Literal data keys must be quoted strings."); White(); Require(':'); if (result.ContainsKey(key)) throw new InvalidDataException("The literal data repeats an object key.");
                result.Add(key, Value(depth + 1)); White(); if (Take('}')) return result; Require(','); White(); if (Take('}')) return result;
            } while (true);
        }
        if (current == '[')
        {
            _position++; var result = new JsonArray(); White(); if (Take(']')) return result;
            do { result.Add(Value(depth + 1)); White(); if (Take(']')) return result; Require(','); White(); if (Take(']')) return result; } while (true);
        }
        foreach (var literal in new[] { "true", "false", "null" })
            if (text.AsSpan(_position).StartsWith(literal, StringComparison.Ordinal)) { _position += literal.Length; return literal == "null" ? null : JsonValue.Create(literal == "true"); }
        var start = _position;
        while (_position < text.Length && text[_position] is >= '0' and <= '9' or '-' or '+' or '.' or 'e' or 'E') _position++;
        if (start == _position || !double.TryParse(text.AsSpan(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            throw new InvalidDataException("The publisher data contains a computed or invalid value.");
        return JsonValue.Create(number);
    }
    private string Identifier()
    {
        var start = _position;
        static bool Letter(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_' or '$';
        if (_position >= text.Length || !Letter(text[_position])) throw new InvalidDataException("A literal property name is required.");
        while (_position < text.Length && (Letter(text[_position]) || text[_position] is >= '0' and <= '9')) _position++;
        return text[start.._position];
    }
    private string String()
    {
        var quote = text[_position++]; var value = new StringBuilder();
        while (_position < text.Length)
        {
            var current = text[_position++]; if (current == quote) return value.ToString();
            if (current < 32) throw new InvalidDataException("The literal string contains an unescaped control character.");
            if (current == '\\')
            {
                if (_position >= text.Length) throw new InvalidDataException("The literal string is truncated."); current = text[_position++];
                current = current switch { '\'' => '\'', '"' => '"', '\\' => '\\', '/' => '/', 'n' => '\n', 'r' => '\r', 't' => '\t', 'b' => '\b', 'f' => '\f',
                    'u' => Hex(4), 'x' => Hex(2), _ => throw new InvalidDataException("The literal string uses an unsupported escape.") };
            }
            value.Append(current); if (value.Length > 1024 * 1024) throw new InvalidDataException("The literal string exceeds its limit.");
        }
        throw new InvalidDataException("The literal string is unterminated.");
    }
    private char Hex(int length)
    {
        if (_position + length > text.Length || !ushort.TryParse(text.AsSpan(_position, length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException("The literal string contains an invalid character escape.");
        _position += length; return (char)value;
    }
    private void White() { while (_position < text.Length && char.IsWhiteSpace(text[_position])) _position++; }
    private bool Take(char value) { if (_position >= text.Length || text[_position] != value) return false; _position++; return true; }
    private void Require(char value) { if (!Take(value)) throw new InvalidDataException("The publisher data is not a supported literal object."); }
}

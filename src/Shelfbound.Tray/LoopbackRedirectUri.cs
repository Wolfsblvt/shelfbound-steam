using System.Globalization;
using System.Text.RegularExpressions;

namespace Shelfbound.Tray;

/// <summary>
/// A native-connect callback URI accepted by the frozen cloud contract. Validation is deliberately
/// lexical: parser normalization must not turn an attacker-controlled spelling into a loopback host.
/// </summary>
internal sealed record LoopbackRedirectUri
{
    private static readonly Regex Pattern = new(
        @"\Ahttp://(?:127\.0\.0\.1|\[::1\]):(?<port>[1-9][0-9]{0,4})/\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private LoopbackRedirectUri(string value, int port)
    {
        Value = value;
        Port = port;
    }

    public string Value { get; }
    public int Port { get; }

    public static bool TryCreate(string? value, out LoopbackRedirectUri? redirectUri)
    {
        redirectUri = null;
        if (string.IsNullOrEmpty(value) || value.Length > 2048 || value.Any(char.IsControl))
            return false;

        Match match = Pattern.Match(value);
        if (!match.Success ||
            !int.TryParse(match.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) ||
            port is < 1 or > 65535)
        {
            return false;
        }

        redirectUri = new LoopbackRedirectUri(value, port);
        return true;
    }

    public override string ToString() => Value;
}

/// <summary>Strictly extracts the one code and state from an exact loopback callback request.</summary>
internal static class LoopbackCallback
{
    private const int CodeLength = 4 + 43;

    public static bool TryRead(
        string httpMethod,
        bool isSecureConnection,
        string? hostHeader,
        string? rawTarget,
        LoopbackRedirectUri expectedRedirectUri,
        string expectedState,
        out string? code)
    {
        code = null;
        if (!string.Equals(httpMethod, "GET", StringComparison.Ordinal) ||
            isSecureConnection ||
            string.IsNullOrEmpty(hostHeader) ||
            string.IsNullOrEmpty(rawTarget) ||
            hostHeader.Any(char.IsControl) ||
            rawTarget.Any(char.IsControl) ||
            !rawTarget.StartsWith("/?", StringComparison.Ordinal) ||
            rawTarget.Contains('#', StringComparison.Ordinal) ||
            rawTarget.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        string requestRedirectUri = $"http://{hostHeader}/";
        if (!LoopbackRedirectUri.TryCreate(requestRedirectUri, out LoopbackRedirectUri? parsedRedirectUri) ||
            parsedRedirectUri is null ||
            !string.Equals(parsedRedirectUri.Value, expectedRedirectUri.Value, StringComparison.Ordinal))
        {
            return false;
        }

        string query = rawTarget[2..];
        string[] fields = query.Split('&');
        if (fields.Length != 2)
            return false;

        string? returnedCode = null;
        string? returnedState = null;
        foreach (string field in fields)
        {
            int separator = field.IndexOf('=');
            if (separator <= 0 || separator == field.Length - 1)
                return false;

            string name = field[..separator];
            if (!TryDecode(field[(separator + 1)..], out string? value))
                return false;

            switch (name)
            {
                case "code" when returnedCode is null:
                    returnedCode = value;
                    break;
                case "state" when returnedState is null:
                    returnedState = value;
                    break;
                default:
                    return false;
            }
        }

        if (!string.Equals(returnedState, expectedState, StringComparison.Ordinal) ||
            !IsConnectCode(returnedCode))
        {
            return false;
        }

        code = returnedCode;
        return true;
    }

    private static bool TryDecode(string rawValue, out string? value)
    {
        value = null;
        for (int index = 0; index < rawValue.Length; index++)
        {
            if (rawValue[index] != '%')
                continue;
            if (index + 2 >= rawValue.Length || !IsHex(rawValue[index + 1]) || !IsHex(rawValue[index + 2]))
                return false;
            index += 2;
        }

        try
        {
            value = Uri.UnescapeDataString(rawValue);
            return !value.Any(char.IsControl);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool IsConnectCode(string? value) =>
        value is { Length: CodeLength } &&
        value.StartsWith("sbc_", StringComparison.Ordinal) &&
        value[4..].All(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    private static bool IsHex(char value) =>
        value is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';
}

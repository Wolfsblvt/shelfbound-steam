using System.Text.RegularExpressions;

namespace Shelfbound.Mcp.Logging;

/// <summary>
/// Masks common secret-bearing query parameters before text reaches the MCP stderr logging sink.
/// Steam requires its Web API key in the request query, so this is defence in depth alongside
/// suppressing the named HTTP client's Information logs.
/// </summary>
internal static partial class SecretMasking
{
    public const string Redacted = "***";

    // The parameter name is retained for diagnostics. Values run to a query separator, whitespace, or
    // quote so the same protection covers URIs, structured scope text, and exception messages.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_-])(?<name>client_secret|access_token|api[_-]?key|key|token)=(?<value>[^&\s\""']+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretParameter();

    /// <summary>
    /// Attempts to redact every supported secret parameter. Failure is reported to the caller so a log
    /// sink can suppress the event rather than risk emitting the original text.
    /// </summary>
    public static bool TryMask(string input, out string masked)
    {
        try
        {
            masked = string.IsNullOrEmpty(input) || !input.Contains('=')
                ? input
                : SecretParameter().Replace(input, $"${{name}}={Redacted}");
            return true;
        }
        catch
        {
            masked = string.Empty;
            return false;
        }
    }
}

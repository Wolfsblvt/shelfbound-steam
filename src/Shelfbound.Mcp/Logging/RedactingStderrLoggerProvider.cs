using System.Text;
using Microsoft.Extensions.Logging;

namespace Shelfbound.Mcp.Logging;

/// <summary>
/// The local MCP's stderr logging boundary. It redacts secret-bearing query values from rendered
/// messages, optional scopes, and exception text before anything is written. If rendering or masking
/// fails, the original event is discarded rather than risking a credential disclosure.
/// </summary>
public sealed class RedactingStderrLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly bool _includeScopes;
    private readonly TextWriter _output;
    private readonly object _writeLock = new();
    private IExternalScopeProvider? _scopeProvider;

    /// <summary>Creates the production sink, which writes only to stderr.</summary>
    public RedactingStderrLoggerProvider()
        : this(Console.Error)
    {
    }

    internal RedactingStderrLoggerProvider(TextWriter output, bool includeScopes = false)
    {
        _output = output;
        _includeScopes = includeScopes;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new RedactingStderrLogger(categoryName, this);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    private IDisposable BeginScope<TState>(TState state)
        where TState : notnull => _scopeProvider?.Push(state) ?? NoopScope.Instance;

    private void Write<TState>(
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!TryRender(formatter, state, exception, out string message) ||
            !TryMask(categoryName, out string category))
        {
            WriteSuppressedEvent();
            return;
        }

        List<string>? scopes = _includeScopes ? TryRenderScopes() : null;
        if (_includeScopes && scopes is null)
        {
            WriteSuppressedEvent();
            return;
        }

        string? exceptionText = null;
        if (exception is not null && !TryMask(exception.ToString(), out exceptionText))
        {
            WriteSuppressedEvent();
            return;
        }

        var output = new StringBuilder();
        output.Append(logLevel.ToString().ToLowerInvariant())
            .Append(": ")
            .Append(category)
            .Append('[')
            .Append(eventId.Id)
            .AppendLine("]")
            .Append("      ")
            .AppendLine(message);

        foreach (string scope in scopes ?? [])
            output.Append("      => ").AppendLine(scope);

        if (exceptionText is not null)
            output.AppendLine(exceptionText);

        lock (_writeLock)
            _output.Write(output.ToString());
    }

    private List<string>? TryRenderScopes()
    {
        var scopes = new List<string>();
        try
        {
            _scopeProvider?.ForEachScope(
                static (scope, collected) => collected.Add(scope?.ToString() ?? string.Empty),
                scopes);

            for (int index = 0; index < scopes.Count; index++)
            {
                if (!TryMask(scopes[index], out string masked))
                    return null;
                scopes[index] = masked;
            }

            return scopes;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryRender<TState>(
        Func<TState, Exception?, string> formatter,
        TState state,
        Exception? exception,
        out string message)
    {
        try
        {
            return TryMask(formatter(state, exception), out message);
        }
        catch
        {
            message = string.Empty;
            return false;
        }
    }

    private static bool TryMask(string input, out string masked) => SecretMasking.TryMask(input, out masked);

    private void WriteSuppressedEvent()
    {
        lock (_writeLock)
            _output.WriteLine("warning: Shelfbound.Mcp.Logging[0]");
    }

    private sealed class RedactingStderrLogger(string categoryName, RedactingStderrLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            provider.Write(categoryName, logLevel, eventId, state, exception, formatter);
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}

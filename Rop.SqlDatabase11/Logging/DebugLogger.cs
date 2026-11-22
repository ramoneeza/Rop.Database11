using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Rop.Database11.Logging;

/// <summary>
/// Minimal ILogger that writes to <see cref="Debug.Print(string?)"/>.
/// Used as a fallback when the consumer does not provide its own logger,
/// preserving the historical behavior of the library.
/// </summary>
internal sealed class DebugLogger : ILogger
{
    public static readonly DebugLogger Instance = new();

    private DebugLogger()
    {
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Debug.Print(formatter(state, exception));
    }
}

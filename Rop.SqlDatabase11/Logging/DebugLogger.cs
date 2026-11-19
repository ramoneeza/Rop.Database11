using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Rop.Database11.Logging;

/// <summary>
/// ILogger mínimo que escribe en <see cref="Debug.Print(string?)"/>.
/// Se usa como fallback cuando el consumidor no proporciona un logger propio,
/// preservando el comportamiento histórico de la librería.
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

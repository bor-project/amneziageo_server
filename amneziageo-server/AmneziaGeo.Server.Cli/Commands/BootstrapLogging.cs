using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// Prints installation diagnostics.
/// </summary>
internal sealed class BootstrapLogging : ILoggerProvider, ILogger
{
    private readonly bool _verbose;

    /// <summary>
    /// ctor
    /// </summary>
    public BootstrapLogging(bool verbose)
    {
        _verbose = verbose;
    }

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= (_verbose ? LogLevel.Debug : LogLevel.Error);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        Console.Error.WriteLine(formatter(state, exception));
        if (exception is not null)
        {
            Console.Error.WriteLine(exception);
        }
    }

    public void Dispose()
    {
    }
}

using Microsoft.Extensions.Logging;

namespace MidiRestyle.Mcp;

/// <summary>
/// The only logger the server has. stdout belongs to the protocol - a single stray line on it is a
/// malformed JSON-RPC frame and the session is over - so everything goes to stderr, one line per
/// event, where the host's own log collects it.
/// </summary>
/// <remarks>
/// Hand-rolled rather than pulling in <c>Microsoft.Extensions.Logging.Console</c>: that would add a
/// package to redistribute and a notice to maintain, and its default provider writes to
/// <see cref="Console.Out"/> - the one stream this server must never touch. A sink that cannot be
/// pointed at stdout is worth forty lines.
/// </remarks>
public sealed class StderrLoggerFactory(LogLevel minimum = LogLevel.Information) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName, minimum);

    /// <summary>
    /// Ignored by design: the sink is fixed. Silently accepting a provider is deliberate rather than
    /// throwing, because the SDK may register one and a server that refuses to start over a log
    /// destination would be worse than one that logs where it always logs.
    /// </summary>
    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class StderrLogger(string category, LogLevel minimum) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            string line = $"[{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            Console.Error.WriteLine(line);
        }
    }
}

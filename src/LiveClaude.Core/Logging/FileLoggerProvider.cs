using Microsoft.Extensions.Logging;

namespace LiveClaude.Core.Logging;

/// <summary>
/// Minimal file logger. The supervisor runs windowless, so its own diagnostics need somewhere to go:
/// %ProgramData%\LiveClaude\logs\supervisor.log.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly RollingLogWriter _writer;
    private readonly LogLevel _minimum;

    public FileLoggerProvider(string path, LogLevel minimum = LogLevel.Information, int maxSizeMb = 8)
    {
        _writer = new RollingLogWriter(path, maxSizeMb);
        _minimum = minimum;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(_writer, categoryName, _minimum);

    public void Dispose() => _writer.Dispose();

    private sealed class FileLogger(RollingLogWriter writer, string category, LogLevel minimum) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var shortCategory = category.Split('.').LastOrDefault() ?? category;
            var message = $"[{logLevel.ToString().ToLowerInvariant()}] {shortCategory}: {formatter(state, exception)}";

            if (exception is not null)
                message += $"{Environment.NewLine}{exception}";

            writer.Write(message);
        }
    }
}

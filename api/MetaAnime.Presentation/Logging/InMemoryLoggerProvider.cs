using MetaAnime.Core.Logging;

namespace MetaAnime.Presentation.Logging;

/// <summary>Teed logger provider that mirrors the API's own log stream into <see cref="InMemoryLogStore"/>.</summary>
public sealed class InMemoryLoggerProvider(InMemoryLogStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new StoreLogger(store, categoryName);

    public void Dispose()
    {
        // The store is a singleton owned by DI.
    }

    private sealed class StoreLogger(InMemoryLogStore store, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            store.Add(new LogEntry(
                DateTimeOffset.UtcNow,
                logLevel.ToString(),
                ShortCategory(category),
                formatter(state, exception),
                exception?.ToString()));
        }

        /// <summary>Trim the namespace noise but keep the class name.</summary>
        private static string ShortCategory(string category)
        {
            var lastDot = category.LastIndexOf('.');
            return lastDot > 0 && lastDot < category.Length - 1 ? category[(lastDot + 1)..] : category;
        }
    }
}

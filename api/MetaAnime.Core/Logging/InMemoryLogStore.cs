using System.Collections.Concurrent;

namespace MetaAnime.Core.Logging;

/// <summary>One captured server log line.</summary>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    string Message,
    string? Exception);

/// <summary>
/// Bounded, thread-safe ring buffer of recent log lines so the UI can show what the API is
/// actually doing (job failures, Sonarr timeouts, …) without shelling into the container logs.
/// </summary>
public sealed class InMemoryLogStore
{
    public const int Capacity = 2000;

    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly object _trimLock = new();

    public void Add(LogEntry entry)
    {
        _entries.Enqueue(entry);

        if (_entries.Count <= Capacity) return;

        lock (_trimLock)
        {
            while (_entries.Count > Capacity && _entries.TryDequeue(out _))
            {
                // drop oldest
            }
        }
    }

    /// <summary>Newest first, bounded by <paramref name="take"/>.</summary>
    public IReadOnlyList<LogEntry> Snapshot(int take, string? level = null, string? search = null)
    {
        IEnumerable<LogEntry> query = _entries.Reverse();

        if (!string.IsNullOrWhiteSpace(level) && !level.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            // "warn" also matches "Warning"; comparisons are case-insensitive prefixes.
            query = query.Where(e => e.Level.StartsWith(level, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(e =>
                e.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (e.Exception?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return query.Take(Math.Clamp(take, 1, 1000)).ToList();
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
            // drain
        }
    }
}

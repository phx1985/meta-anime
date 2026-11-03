using System.Diagnostics;

namespace MetaAnime.Application.Services.AniList;

/// <summary>
/// Singleton pacing gate for ALL outbound AniList GraphQL requests. Requests QUEUE behind each
/// other (never burst): at least 400 ms between dispatches and at most 55 dispatches per 60 s
/// sliding window — well inside AniList's unauthenticated ~90 req/min budget. A queued request
/// is dropped only when its queue wait would exceed 15 s, which keeps the shared egress IP clear
/// of rate-limit penalties without deadlocking callers.
/// </summary>
public sealed class AniListPacingGate
{
    private static readonly TimeSpan DefaultMinInterval  = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan DefaultWindow       = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultMaxQueueWait = TimeSpan.FromSeconds(15);
    private const int DefaultMaxPerWindow = 55;

    private readonly TimeSpan _minInterval;
    private readonly int _maxPerWindow;
    private readonly TimeSpan _window;
    private readonly TimeSpan _maxQueueWait;

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Queue<DateTime> _dispatched = new();
    private DateTime _lastDispatched = DateTime.MinValue;

    /// <summary>Production pacing values by default; overridable for tests.</summary>
    public AniListPacingGate(
        TimeSpan? minInterval = null,
        int maxPerWindow = DefaultMaxPerWindow,
        TimeSpan? window = null,
        TimeSpan? maxQueueWait = null)
    {
        _minInterval = minInterval ?? DefaultMinInterval;
        _maxPerWindow = maxPerWindow;
        _window = window ?? DefaultWindow;
        _maxQueueWait = maxQueueWait ?? DefaultMaxQueueWait;
    }

    /// <summary>
    /// Queues for a dispatch slot. Returns <c>true</c> when the caller may send now,
    /// <c>false</c> when the request was dropped because its queue wait exceeded the budget
    /// (or the caller was cancelled).
    /// </summary>
    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var enqueuedAt = Stopwatch.StartNew();

        try
        {
            await _mutex.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        try
        {
            while (true)
            {
                var now = DateTime.UtcNow;

                // Sliding window: forget dispatches older than the window.
                while (_dispatched.Count > 0 && now - _dispatched.Peek() >= _window)
                    _dispatched.Dequeue();

                var waitFor = TimeSpan.Zero;

                // Per-minute budget — when exhausted, wait until the oldest dispatch ages out.
                if (_dispatched.Count >= _maxPerWindow)
                    waitFor = _dispatched.Peek() + _window - now;

                // Minimum spacing between consecutive dispatches.
                var sinceLast = now - _lastDispatched;
                if (sinceLast < _minInterval)
                {
                    var intervalWait = _minInterval - sinceLast;
                    if (intervalWait > waitFor)
                        waitFor = intervalWait;
                }

                if (waitFor <= TimeSpan.Zero)
                {
                    _lastDispatched = now;
                    _dispatched.Enqueue(now);
                    return true;
                }

                // Drop only when the queue wait would exceed the budget — never burst, never hang.
                if (enqueuedAt.Elapsed + waitFor >= _maxQueueWait)
                    return false;

                try
                {
                    await Task.Delay(waitFor, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }
        finally
        {
            _mutex.Release();
        }
    }
}

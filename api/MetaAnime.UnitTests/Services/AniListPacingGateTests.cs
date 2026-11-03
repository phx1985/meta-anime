using System.Diagnostics;
using FluentAssertions;
using MetaAnime.Application.Services.AniList;

namespace MetaAnime.UnitTests.Services;

public class AniListPacingGateTests
{
    [Fact]
    public async Task SequentialAcquires_AreSpacedByMinInterval()
    {
        var gate = new AniListPacingGate(minInterval: TimeSpan.FromMilliseconds(120), maxQueueWait: TimeSpan.FromSeconds(10));

        (await gate.TryAcquireAsync(CancellationToken.None)).Should().BeTrue();

        var stopwatch = Stopwatch.StartNew();
        (await gate.TryAcquireAsync(CancellationToken.None)).Should().BeTrue();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task Request_IsDroppedOnlyWhenQueueWaitWouldExceedBudget()
    {
        // 500 ms spacing but only 50 ms of queue-wait budget: the second request must be dropped fast.
        var gate = new AniListPacingGate(minInterval: TimeSpan.FromMilliseconds(500), maxQueueWait: TimeSpan.FromMilliseconds(50));
        (await gate.TryAcquireAsync(CancellationToken.None)).Should().BeTrue();

        var stopwatch = Stopwatch.StartNew();
        (await gate.TryAcquireAsync(CancellationToken.None)).Should().BeFalse();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public async Task WindowBudgetExhaustion_DropsInsteadOfBursting()
    {
        // 2 dispatches per 300 ms window with negligible spacing: the third would wait ~300 ms
        // for the window to slide, far beyond its 100 ms queue-wait budget.
        var gate = new AniListPacingGate(
            minInterval: TimeSpan.FromMilliseconds(1),
            maxPerWindow: 2,
            window: TimeSpan.FromMilliseconds(300),
            maxQueueWait: TimeSpan.FromMilliseconds(100));

        (await gate.TryAcquireAsync(CancellationToken.None)).Should().BeTrue();
        (await gate.TryAcquireAsync(CancellationToken.None)).Should().BeTrue();
        (await gate.TryAcquireAsync(CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentAcquires_QueueAndNeverBurst()
    {
        var gate = new AniListPacingGate(minInterval: TimeSpan.FromMilliseconds(40), maxQueueWait: TimeSpan.FromSeconds(10));
        var dispatchTimes = new System.Collections.Concurrent.ConcurrentQueue<long>();
        var stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            if (await gate.TryAcquireAsync(CancellationToken.None))
                dispatchTimes.Enqueue(stopwatch.ElapsedMilliseconds);
        }));

        var times = dispatchTimes.OrderBy(t => t).ToList();
        times.Should().HaveCount(5);
        for (var i = 1; i < times.Count; i++)
            (times[i] - times[i - 1]).Should().BeGreaterThanOrEqualTo(30); // min interval, with clock tolerance
    }

    [Fact]
    public async Task CancelledCaller_IsDroppedNotThrown()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var gate = new AniListPacingGate();
        (await gate.TryAcquireAsync(cts.Token)).Should().BeFalse();
    }
}

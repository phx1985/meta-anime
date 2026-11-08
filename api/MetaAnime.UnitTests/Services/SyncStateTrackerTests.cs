using Mediator;
using MetaAnime.Application.BackgroundJobs;
using MetaAnime.Application.Services;
using MetaAnime.Core.Interfaces.Repositories;
using MetaAnime.Application.Interfaces;
using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Hangfire;

namespace MetaAnime.UnitTests.Services;

/// <summary>
/// Pins the sync-state contract the UI depends on: the activity flag/timestamp, the
/// first-sync chaining, and the fact that a concurrency conflict is contained instead of
/// failing the whole Hangfire job.
/// </summary>
public class SyncStateTrackerTests
{
    [Fact]
    public void SetActivitySyncRunning_SetsFlagAndUpdatesLastActivityDate()
    {
        var tracker = new InMemorySyncStateTracker();

        Assert.False(tracker.IsActivitySyncRunning);
        Assert.Null(tracker.LastActivitySyncDate);

        tracker.SetActivitySyncRunning(true);
        Assert.True(tracker.IsActivitySyncRunning);
        Assert.Null(tracker.LastActivitySyncDate);

        tracker.SetActivitySyncRunning(false, succeeded: true);

        Assert.False(tracker.IsActivitySyncRunning);
        Assert.NotNull(tracker.LastActivitySyncDate);
    }

    [Fact]
    public async Task FirstFullSync_EnqueuesActivitySyncWhenLastDateNull()
    {
        var matching = new Mock<IAnimeMatchingService>();
        matching
            .Setup(m => m.MatchAsync(It.IsAny<bool>(), It.IsAny<ISyncProgressNotifier?>()))
            .ReturnsAsync(Result.Success(new MatchReport(0, 0, 0, Array.Empty<UnmatchedCandidate>())));

        var jobs = new Mock<IBackgroundJobClient>();
        var tracker = new InMemorySyncStateTracker();

        var job = new AnimeSyncJob(
            matching.Object,
            Mock.Of<IAnimeMediatorService>(),
            Mock.Of<ICacheEntryRepository>(),
            Mock.Of<ISyncProgressNotifier>(),
            tracker,
            ActivityLog(),
            jobs.Object,
            NullLogger<AnimeSyncJob>.Instance);

        await job.SyncAnimeMetadata();

        // Specifically the activity job — the codec enrichment is enqueued on every run, so a
        // bare "at least once" would pass even if the chaining were removed.
        jobs.Verify(
            j => j.Create(
                It.Is<Hangfire.Common.Job>(job => job.Type == typeof(DownloadStatsCacheJob)),
                It.IsAny<Hangfire.States.IState>()),
            Times.Once);
        Assert.NotNull(tracker.LastSyncDate);
    }

    [Fact]
    public async Task ConcurrencyConflict_DoesNotRethrowAndBroadcastsError()
    {
        var matching = new Mock<IAnimeMatchingService>();
        matching
            .Setup(m => m.MatchAsync(It.IsAny<bool>(), It.IsAny<ISyncProgressNotifier?>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var notifier = new Mock<ISyncProgressNotifier>();
        var tracker = new InMemorySyncStateTracker();

        var job = new AnimeSyncJob(
            matching.Object,
            Mock.Of<IAnimeMediatorService>(),
            Mock.Of<ICacheEntryRepository>(),
            notifier.Object,
            tracker,
            ActivityLog(),
            Mock.Of<IBackgroundJobClient>(),
            NullLogger<AnimeSyncJob>.Instance);

        // Must not throw: containment is what stops Hangfire retry storms.
        await job.SyncAnimeMetadata();

        notifier.Verify(n => n.NotifySyncFailedAsync(It.IsAny<string>()), Times.Once);
        Assert.Null(tracker.LastSyncDate); // failed run does not stamp the completion date
    }

    /// <summary>Activity logging is fire-and-forget; the job only needs a no-op instance.</summary>
    private static ActivityLogService ActivityLog() =>
        new(Mock.Of<IActivityLogRepository>(), Mock.Of<IPropertyBagRepository>(), Mock.Of<ICacheEntryRepository>(), Mock.Of<IAnimeMappingRepository>(), Mock.Of<ISonarrHistoryEventRepository>(), NullLogger<ActivityLogService>.Instance);

    /// <summary>
    /// Named exactly like EF's exception: the Application layer has no EF reference by design,
    /// so the job matches on the type name and this stand-in exercises that path.
    /// </summary>
    private sealed class DbUpdateConcurrencyException : Exception
    {
        public DbUpdateConcurrencyException()
            : base("The database operation was expected to affect 1 row(s), but actually affected 0 row(s).") { }
    }
}

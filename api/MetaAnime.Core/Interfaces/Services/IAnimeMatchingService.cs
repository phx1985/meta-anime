using MetaAnime.Core.Interfaces.Shared.Results;
using MetaAnime.Core.Models;

namespace MetaAnime.Core.Interfaces.Services;

public interface IAnimeMatchingService
{
    /// <summary>
    /// Runs the full matching pipeline. Returns a <see cref="MatchReport"/> with per-tier
    /// mapping counts and the unmatched candidates reserved for a future ambiguity queue.
    /// </summary>
    Task<Result<MatchReport>> MatchAsync(bool forceRefreshKometaMapping = false, ISyncProgressNotifier? progressNotifier = null);
}

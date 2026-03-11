using MetaAnime.Core.Models;

namespace MetaAnime.Core.Interfaces.Services;

/// <summary>
/// Singleton read/write cache for the most-recent provider health snapshot.
/// Written by <c>ProviderHealthMonitor</c>; read by the system-health API endpoint.
/// </summary>
public interface IProviderHealthCache
{
    /// <summary>The latest snapshot, or <c>null</c> if the monitor has not run yet.</summary>
    SystemHealthSnapshot? Current { get; }

    /// <summary>Replaces the cached snapshot with a freshly probed result.</summary>
    void Update(SystemHealthSnapshot snapshot);
}

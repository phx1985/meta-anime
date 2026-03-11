using MetaAnime.Core.Interfaces.Services;
using MetaAnime.Core.Models;

namespace MetaAnime.Application.Services;

/// <summary>
/// Thread-safe in-memory singleton that holds the latest provider health snapshot.
/// A <c>volatile</c> reference swap is sufficient — the record is immutable once written.
/// </summary>
public sealed class InMemoryProviderHealthCache : IProviderHealthCache
{
    private volatile SystemHealthSnapshot? _current;

    public SystemHealthSnapshot? Current => _current;

    public void Update(SystemHealthSnapshot snapshot) => _current = snapshot;
}

using MetaAnime.Core.Interfaces.Mappers;
using MetaAnime.Core.Interfaces.Shared;
using MetaAnime.Core.Interfaces.Shared.Results;

namespace MetaAnime.Core.Interfaces.Services;

public interface IPlexAnimeSourceService<T> : IAnimeSourceService<T>
    where T : IAnimeConvertible
{
    Task<Result<string>> GetMachineIdentifierAsync();
    /// <summary>Returns all available Plex library sections (id, name, type).</summary>
    Task<Result<IList<PlexLibraryInfo>>> GetLibrariesAsync();
}

public sealed record PlexLibraryInfo(string Key, string Title, string Type);

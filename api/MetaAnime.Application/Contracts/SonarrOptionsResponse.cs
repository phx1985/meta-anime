namespace MetaAnime.Application.Contracts;

public sealed record SonarrOptionsResponse(
    IList<SonarrRootFolderDto> RootFolders,
    IList<SonarrQualityProfileDto> QualityProfiles,
    IList<int> AnimeTagIds
);

public sealed record SonarrRootFolderDto(int Id, string Path, long FreeSpace);
public sealed record SonarrQualityProfileDto(int Id, string Name);

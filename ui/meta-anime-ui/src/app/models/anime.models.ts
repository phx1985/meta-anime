export enum AnimeSource {
  Unknown = 0,
  MyAnimeList = 1,
  Plex = 2
}

export interface PlexAnimeInfo {
  PlexId: string;
  Title: string;
}

export interface MalAnimeInfo {
  MalId: string;
  Title: string;
}

export interface SonarrAnimeInfo {
  SonarrId: string;
  Title: string;
}

export interface AnimeDetailsResponse {
  AniDbId: string;
  Title: string;
  PlexAnimes?: PlexAnimeInfo[];
  MalAnimes?: MalAnimeInfo[];
  SonarrAnimes?: SonarrAnimeInfo[];
}

export interface AnimeResponse {
  Id: string;
  SourceId: string;
  Title: string;
  Description?: string;
  AnimeSource: AnimeSource;
}

export interface ViewingProgressStats {
  MalEpisodesWatched: number;
  MalTotalEpisodes: number;
  PlexEpisodesWatched: number;
  PlexTotalEpisodes: number;
}

export interface AnimeStatisticsResponse {
  PlexAnime: number;
  MyAnimeListAnime: number;
  SonarrAnime: number;
  MatchedAnime: number;
  GenreStats: { [key: string]: number };
  ViewingProgress: ViewingProgressStats;
}

export interface PagedResponse<T> {
  Data: T[];
  Page: number;
  PageSize: number;
  TotalCount: number;
  TotalPages: number;
  HasNextPage: boolean;
  HasPreviousPage: boolean;
}

export interface AnimeFilters {
  title?: string;
  source?: string;
  status?: string;
  genre?: string;
  sortBy?: string;
  sortOrder?: string;
}

export interface CreateAnimeRequest {
  SourceId: string;
  Title: string;
  Description?: string;
  AnimeSource: AnimeSource;
}

export interface UpdateAnimeRequest {
  Title: string;
  Description?: string;
}

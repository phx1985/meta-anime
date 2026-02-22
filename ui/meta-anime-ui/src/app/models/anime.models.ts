export enum AnimeSource {
  Unknown = 0,
  MyAnimeList = 1,
  Plex = 2
}

export interface PlexAnimeInfo {
  plexId: string;
  title: string;
}

export interface MalAnimeInfo {
  malId: string;
  title: string;
}

export interface SonarrAnimeInfo {
  sonarrId: string;
  title: string;
}

export interface AnimeDetailsResponse {
  aniDbId: string;
  title: string;
  plexAnimes?: PlexAnimeInfo[];
  malAnimes?: MalAnimeInfo[];
  sonarrAnimes?: SonarrAnimeInfo[];
}

export interface AnimeResponse {
  id: string;
  sourceId: string;
  title: string;
  description?: string;
  animeSource: AnimeSource;
}

export interface ViewingProgressStats {
  malEpisodesWatched: number;
  malTotalEpisodes: number;
  plexEpisodesWatched: number;
  plexTotalEpisodes: number;
}

export interface AnimeStatisticsResponse {
  plexAnime: number;
  myAnimeListAnime: number;
  sonarrAnime: number;
  matchedAnime: number;
  sonarrNotInMal: number;
  malNotInPlex: number;
  plexNotInMal: number;
  plexNotInSonarr: number;
  genreStats: { [key: string]: number };
  viewingProgress: ViewingProgressStats;
}

export interface PagedResponse<T> {
  data: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
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
  sourceId: string;
  title: string;
  description?: string;
  animeSource: AnimeSource;
}

export interface UpdateAnimeRequest {
  title: string;
  description?: string;
}

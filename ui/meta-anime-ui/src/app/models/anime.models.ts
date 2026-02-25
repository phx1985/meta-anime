export enum AnimeSource {
  Unknown = 0,
  MyAnimeList = 1,
  Plex = 2
}

export interface MalProgressionInfo {
  episodesWatched: number;
  totalEpisodes: number;
}

export interface MalRatingInfo {
  myScore: number;
  malMean: number;
}

export interface MalAlternativeTitles {
  en?: string;
  ja?: string;
  synonyms?: string[];
}

export interface PlexProgressionInfo {
  viewedEpisodes: number;
  totalEpisodes: number;
}

export interface SonarrSeasonInfo {
  seasonNumber: number;
  availableEpisodes: number;
  totalEpisodes: number;
}

export interface PlexAnimeInfo {
  title: string;
  id?: string;
  url?: string;
  progression?: PlexProgressionInfo;
}

export interface MalAnimeInfo {
  title: string;
  id?: string;
  url?: string;
  viewState?: string;
  addedDate?: string;
  progression?: MalProgressionInfo;
  rating?: MalRatingInfo;
  genres?: string[];
  synopsis?: string;
  startDate?: string;
  endDate?: string;
  alternativeTitles?: MalAlternativeTitles;
  coverImageUrl?: string;
  status?: string;
  mediaType?: string;
}

export interface SonarrAnimeInfo {
  title: string;
  id?: string;
  url?: string;
  exists: boolean;
  seasonInfo?: SonarrSeasonInfo[];
  codecDistribution?: { [key: string]: number };
  sizeOnDisk?: number;
}

export interface AnimeDetailsResponse {
  aniDbId: string;
  title: string;
  description?: string;
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
  status?: string[];
  genre?: string[];
  genreOperator?: 'or' | 'and';
  sortBy?: string;
  sortOrder?: string;
  airingStatus?: string[];
  hasFile?: boolean;
  codec?: string[];
  mediaType?: string[];
}

/** Serialisable snapshot of the filter panel state for saved presets. */
export interface SavedFilterState {
  title?: string;
  genre?: string[];
  genreOperator?: 'or' | 'and';
  watchState?: string[];
  airingStatus?: string[];
  hasFile?: 'any' | 'yes' | 'no';
  codec?: string[];
  mediaType?: string[];
  sortBy?: string;
  sortOrder?: 'asc' | 'desc';
}

export interface FilterPreset {
  id: string;
  /** i18n key for built-in presets, plain label for user presets */
  labelKey: string;
  isBuiltIn: boolean;
  filters: SavedFilterState;
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

export interface SeasonalAnimeItem {
  malId: string;
  title: string;
  synopsis?: string;
  genres?: string[];
  coverImageUrl?: string;
  malScore: number;
  startDate?: string;
  mediaType?: string;
  status?: string;
  malUrl?: string;
  inMal: boolean;
  inPlex: boolean;
  plexUrl?: string;
  inSonarr: boolean;
  sonarrUrl?: string;
  tvdbId?: string;
  englishTitle?: string;
  malViewState?: string;
}

export type SeasonName = 'winter' | 'spring' | 'summer' | 'fall';

/** Serialisable snapshot of the seasonal filter panel state for saved presets. */
export interface SeasonalSavedFilterState {
  genre?: string[];
  genreOperator?: 'or' | 'and';
  malStatus?: string[];
  mediaType?: string[];
  sortBy?: string;
  sortOrder?: 'asc' | 'desc';
}

export interface SeasonalFilterPreset {
  id: string;
  label: string;
  filters: SeasonalSavedFilterState;
}

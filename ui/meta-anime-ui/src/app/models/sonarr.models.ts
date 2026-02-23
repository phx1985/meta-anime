export interface SonarrCodecSearchResponse {
  id: number;
  title: string;
  url: string;
  codecDistribution: { [key: string]: number };
  totalSizeOnDisk: number;
}

export interface SonarrOptionsResponse {
  rootFolders: SonarrRootFolder[];
  qualityProfiles: SonarrQualityProfile[];
  animeTagIds: number[];
}

export interface SonarrRootFolder {
  id: number;
  path: string;
  freeSpace: number;
}

export interface SonarrQualityProfile {
  id: number;
  name: string;
}

export interface SonarrAddSeriesRequest {
  malId: string;
  rootFolderPath: string;
  qualityProfileId: number;
}

export interface MalListStatusRequest {
  status: string;
  numWatchedEpisodes: number;
  score: number;
}

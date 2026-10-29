export interface LibraryAnalysisSummary {
  totalSizeBytes: number;
  totalFileCount: number;
  totalSeriesCount: number;
  missingFileSeriesCount: number;
  avgEpSizeThresholdMb: number;
  avgGibPerHourThreshold: number;
  series: LibrarySeriesRow[];
}

export interface LibrarySeriesRow {
  sonarrId: number;
  title: string;
  path: string;
  ended: boolean;
  fileCount: number;
  totalEpisodes: number;
  totalSizeBytes: number;
  avgEpisodeSizeBytes: number;
  percentDownloaded: number;
  primaryVideoCodec?: string;
  primaryAudioCodec?: string;
  primaryResolution?: string;
  primaryBitDepth?: number;
  videoCodecDistribution?: Record<string, number>;
  audioCodecDistribution?: Record<string, number>;
  resolutionDistribution?: Record<string, number>;
  episodeFiles?: LibraryEpisodeFileInfo[];
  localAnimeId?: string | null;
  totalDurationSeconds: number;
  gibPerHour: number;
  /** Sonarr next-airing date (null when ended or between seasons). Drives the season-aware status filter. */
  nextAiringDate?: string | null;
}

export interface LibraryEpisodeFileInfo {
  episodeFileId: number;
  relativePath: string;
  sizeBytes: number;
  videoCodec: string;
  audioCodec: string;
  resolution: string;
  videoBitDepth: number;
  audioChannels: number;
  qualityProfile: string;
  durationSeconds: number;
  isDurationEstimated: boolean;
}

export interface EnrichSeriesRequest {
  sonarrIds: number[];
}

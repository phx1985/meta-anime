export interface SonarrCodecSearchResponse {
  id: number;
  title: string;
  url: string;
  codecDistribution: { [key: string]: number };
  totalSizeOnDisk: number;
}

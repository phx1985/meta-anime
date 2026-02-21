export interface SonarrCodecSearchResponse {
  Id: number;
  Title: string;
  Url: string;
  CodecDistribution: { [key: string]: number };
  TotalSizeOnDisk: number;
}

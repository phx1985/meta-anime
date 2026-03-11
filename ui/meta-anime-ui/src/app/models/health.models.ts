export type ProviderHealthStatus = 'Unknown' | 'Online' | 'Degraded' | 'Offline';

export interface ProviderHealthResult {
  status: ProviderHealthStatus;
  latencyMs: number;
  message: string | null;
  checkedAt: string;
}

export interface SystemHealthSnapshot {
  plex: ProviderHealthResult;
  sonarr: ProviderHealthResult;
  mal: ProviderHealthResult;
  generatedAt: string;
}

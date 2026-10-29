import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Mirrors AppConfigBackupDto (camelCase JSON). Null fields = leave unchanged on import. */
export interface AppConfigBackup {
  configVersion?: number;
  exportedAt?: string;
  plex?: {
    enabled?: boolean;
    host?: string;
    token?: string;
    animeSections?: string;
    machineIdentifier?: string;
  };
  sonarr?: {
    enabled?: boolean;
    host?: string;
    apiKey?: string;
    remoteUrl?: string;
    animeTags?: string;
    defaultRootFolder?: string;
    defaultQualityProfileName?: string;
    defaultSeasonFolder?: boolean;
    defaultMonitor?: string;
  };
  mal?: {
    clientId?: string;
    clientSecret?: string;
    apiToken?: string;
    accessToken?: string;
    refreshToken?: string;
  };
  botKun?: {
    enabled?: boolean;
    baseUrl?: string;
    apiKey?: string;
    model?: string;
  };
  app?: {
    showNsfw?: boolean;
    syncInterval?: string;
    titleSimilarityThreshold?: string;
    downloadStatsCacheInterval?: string;
    kometaAnimeIdsJsonUrl?: string;
    luckyOnlyFinished?: boolean;
    luckyOnlyPlanToWatch?: boolean;
    luckyOnlyWithFiles?: boolean;
  };
  pathMap?: {
    containerRoot?: string;
    hostRoot?: string;
    separator?: string;
  };
  handBrake?: {
    presetName?: string;
    destDir?: string;
    destMode?: string;
  };
  libraryAnalysis?: {
    avgEpSizeMb?: string;
    avgGibPerHour?: string;
  };
}

export interface AppConfigImportResponse {
  importedValues: number;
  plexConnected: boolean;
  malConnected: boolean;
  sonarrConnected: boolean;
  requiresAuth: boolean;
}

/** Configuration export/import (GET /settings/export, POST /settings/import). */
@Injectable({ providedIn: 'root' })
export class ConfigBackupService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/settings';

  /** Downloads the backup JSON as a file (browser save dialog). */
  exportConfig(): void {
    this.http.get(`${this.base}/export`, { responseType: 'blob' }).subscribe(blob => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `meta-anime-config-${new Date().toISOString().slice(0, 10)}.json`;
      a.click();
      URL.revokeObjectURL(url);
    });
  }

  /** Parses + uploads a backup payload; returns fresh provider connectivity. */
  importConfig(backup: AppConfigBackup): Observable<AppConfigImportResponse> {
    return this.http.post<AppConfigImportResponse>(`${this.base}/import`, backup);
  }

  /** Reads a user-selected .json File into an AppConfigBackup. */
  parseFile(file: File): Observable<AppConfigBackup> {
    return new Observable(subscriber => {
      const reader = new FileReader();
      reader.onload = () => {
        try {
          subscriber.next(JSON.parse(String(reader.result)) as AppConfigBackup);
          subscriber.complete();
        } catch (e) {
          subscriber.error(e);
        }
      };
      reader.onerror = () => subscriber.error(reader.error);
      reader.readAsText(file);
    });
  }
}

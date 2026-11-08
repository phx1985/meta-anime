import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** One server log line (mirrors MetaAnime.Core.Logging.LogEntry). */
export interface ServerLogEntry {
  timestamp: string;
  level: 'Trace' | 'Debug' | 'Information' | 'Warning' | 'Error' | 'Critical' | string;
  category: string;
  message: string;
  exception?: string | null;
}

export interface ServerLogsResponse {
  items: ServerLogEntry[];
  capacity: number;
}

@Injectable({ providedIn: 'root' })
export class LogsService {
  private readonly http = inject(HttpClient);

  getLogs(take = 200, level = 'all', search = ''): Observable<ServerLogsResponse> {
    let params = new HttpParams().set('take', String(take)).set('level', level);
    if (search.trim()) params = params.set('search', search.trim());
    return this.http.get<ServerLogsResponse>('/api/v1/logs', { params });
  }

  clearLogs(): Observable<void> {
    return this.http.delete<void>('/api/v1/logs');
  }
}

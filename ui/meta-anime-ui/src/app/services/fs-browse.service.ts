import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface FsBrowseEntry {
  name: string;
  path: string;
}

export interface FsBrowseResult {
  path: string;
  parent: string | null;
  directories: FsBrowseEntry[];
}

/** Server-side directory listing for the path-mapping folder picker. */
@Injectable({ providedIn: 'root' })
export class FsBrowseService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/fs';

  browse(path?: string): Observable<FsBrowseResult> {
    const params = path ? new HttpParams().set('path', path) : undefined;
    return this.http.get<FsBrowseResult>(`${this.base}/browse`, { params });
  }
}

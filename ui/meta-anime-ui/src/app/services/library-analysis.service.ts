import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  LibraryAnalysisSummary,
  LibrarySeriesRow,
  EnrichSeriesRequest,
} from '../models/library-analysis.models';

@Injectable({ providedIn: 'root' })
export class LibraryAnalysisService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/library-analysis';

  getSummary(): Observable<LibraryAnalysisSummary> {
    return this.http.get<LibraryAnalysisSummary>(this.base);
  }

  enrichSeries(sonarrIds: number[]): Observable<LibrarySeriesRow[]> {
    return this.http.post<LibrarySeriesRow[]>(`${this.base}/enrich`, {
      sonarrIds,
    } satisfies EnrichSeriesRequest);
  }
}

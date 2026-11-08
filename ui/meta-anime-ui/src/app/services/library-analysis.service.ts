import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, concatMap, from, of, reduce } from 'rxjs';
import { catchError } from 'rxjs/operators';
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

  /**
   * Phase 2 enrichment, chunked.
   *
   * One POST for the whole library (700+ series) takes minutes of per-series Sonarr calls; a
   * reload, navigation or proxy timeout then aborts it mid-flight and the API drowns in
   * cancellation noise. Batches keep every request short and let the panels fill progressively.
   */
  enrichSeries(sonarrIds: number[]): Observable<LibrarySeriesRow[]> {
    if (sonarrIds.length <= LibraryAnalysisService.ENRICH_BATCH) {
      return this.postEnrich(sonarrIds);
    }

    const batches: number[][] = [];
    for (let i = 0; i < sonarrIds.length; i += LibraryAnalysisService.ENRICH_BATCH) {
      batches.push(sonarrIds.slice(i, i + LibraryAnalysisService.ENRICH_BATCH));
    }

    return from(batches).pipe(
      concatMap((batch) =>
        this.postEnrich(batch).pipe(
          // A failed batch must not blank the rows already enriched.
          catchError(() => of([] as LibrarySeriesRow[])),
        ),
      ),
      reduce((all, rows) => all.concat(rows), [] as LibrarySeriesRow[]),
    );
  }

  private postEnrich(sonarrIds: number[]): Observable<LibrarySeriesRow[]> {
    return this.http.post<LibrarySeriesRow[]>(`${this.base}/enrich`, {
      sonarrIds,
    } satisfies EnrichSeriesRequest);
  }

  private static readonly ENRICH_BATCH = 100;
}

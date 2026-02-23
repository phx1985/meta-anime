import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SonarrCodecSearchResponse, SonarrOptionsResponse, SonarrAddSeriesRequest, SonarrLookupResult } from '../models/sonarr.models';

@Injectable({ providedIn: 'root' })
export class SonarrService {
  private http = inject(HttpClient);
  private base = '/api/v1/sonarr';

  getSeries(codec?: string): Observable<SonarrCodecSearchResponse[]> {
    let params = new HttpParams();
    if (codec) params = params.set('codec', codec);
    return this.http.get<SonarrCodecSearchResponse[]>(`${this.base}/series`, { params });
  }

  getOptions(): Observable<SonarrOptionsResponse> {
    return this.http.get<SonarrOptionsResponse>(`${this.base}/options`);
  }

  lookup(term: string): Observable<SonarrLookupResult[]> {
    const params = new HttpParams().set('term', term);
    return this.http.get<SonarrLookupResult[]>(`${this.base}/lookup`, { params });
  }

  addSeries(request: SonarrAddSeriesRequest): Observable<unknown> {
    return this.http.post(`${this.base}/add`, request);
  }
}

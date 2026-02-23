import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AnimeStatisticsResponse,
  AnimeResponse,
  AnimeDetailsResponse,
  PagedResponse,
  AnimeFilters,
  CreateAnimeRequest,
  UpdateAnimeRequest
} from '../models/anime.models';

@Injectable({ providedIn: 'root' })
export class AnimeService {
  private http = inject(HttpClient);
  private base = '/api/v1/animes';

  getStatistics(): Observable<AnimeStatisticsResponse> {
    return this.http.get<AnimeStatisticsResponse>(`${this.base}/statistics`);
  }

  getAnimes(page = 1, pageSize = 20, filters?: AnimeFilters, enriched?: boolean): Observable<PagedResponse<AnimeDetailsResponse>> {
    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', pageSize);
    if (filters?.title) params = params.set('title', filters.title);
    if (filters?.source) params = params.set('source', filters.source);
    if (filters?.status?.length) filters.status.forEach(s => (params = params.append('status', s)));
    if (filters?.genre?.length) {
      filters.genre.forEach(g => (params = params.append('genre', g)));
      if (filters.genreOperator) params = params.set('genreOperator', filters.genreOperator);
    }
    if (filters?.sortBy) params = params.set('sortBy', filters.sortBy);
    if (filters?.sortOrder) params = params.set('sortOrder', filters.sortOrder);
    if (filters?.airingStatus?.length) filters.airingStatus.forEach(s => (params = params.append('airingStatus', s)));
    if (filters?.hasFile !== undefined) params = params.set('hasFile', filters.hasFile);
    if (filters?.codec?.length) filters.codec.forEach(c => (params = params.append('codec', c)));
    if (filters?.mediaType?.length) filters.mediaType.forEach(t => (params = params.append('mediaType', t)));
    if (enriched !== undefined) params = params.set('enriched', enriched);
    return this.http.get<PagedResponse<AnimeDetailsResponse>>(this.base, { params });
  }

  getAnime(id: string): Observable<AnimeDetailsResponse> {
    return this.http.get<AnimeDetailsResponse>(`${this.base}/${id}`);
  }

  createAnime(request: CreateAnimeRequest): Observable<AnimeResponse> {
    return this.http.post<AnimeResponse>(this.base, request);
  }

  updateAnime(id: string, request: UpdateAnimeRequest): Observable<AnimeResponse> {
    return this.http.put<AnimeResponse>(`${this.base}/${id}`, request);
  }

  deleteAnime(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  syncAnimes(forceRefreshKometaMapping?: boolean): Observable<void> {
    let params = new HttpParams();
    if (forceRefreshKometaMapping !== undefined) {
      params = params.set('forceRefreshKometaMapping', forceRefreshKometaMapping);
    }
    return this.http.post<void>(`${this.base}/sync`, null, { params });
  }
}

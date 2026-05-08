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
  UpdateAnimeRequest,
  SeasonalAnimeItem,
  FranchiseConstellationData,
  CoverageGapItem,
  CrossLinkEntry,
  MalSearchResult,
} from '../models/anime.models';
import { MalListStatusRequest } from '../models/sonarr.models';

@Injectable({ providedIn: 'root' })
export class AnimeService {
  private http = inject(HttpClient);
  private base = '/api/v1/animes';
  private seasonalBase = '/api/v1/seasonal';
  private malBase = '/api/v1/mal';
  private sonarrBase = '/api/v1/sonarr';

  getStatistics(): Observable<AnimeStatisticsResponse> {
    return this.http.get<AnimeStatisticsResponse>(`${this.base}/statistics`);
  }

  getAnimes(
    page = 1,
    pageSize = 20,
    filters?: AnimeFilters,
    enriched?: boolean,
  ): Observable<PagedResponse<AnimeDetailsResponse>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (filters?.title) params = params.set('title', filters.title);
    if (filters?.source) params = params.set('source', filters.source);
    if (filters?.status?.length)
      filters.status.forEach((s) => (params = params.append('status', s)));
    if (filters?.genre?.length) {
      filters.genre.forEach((g) => (params = params.append('genre', g)));
      if (filters.genreOperator) params = params.set('genreOperator', filters.genreOperator);
    }
    if (filters?.theme?.length) {
      filters.theme.forEach((t) => (params = params.append('theme', t)));
      if (filters.themeOperator) params = params.set('themeOperator', filters.themeOperator);
    }
    if (filters?.demographic?.length) {
      filters.demographic.forEach((d) => (params = params.append('demographic', d)));
      if (filters.demographicOperator)
        params = params.set('demographicOperator', filters.demographicOperator);
    }
    if (filters?.sortBy) params = params.set('sortBy', filters.sortBy);
    if (filters?.sortOrder) params = params.set('sortOrder', filters.sortOrder);
    if (filters?.airingStatus?.length)
      filters.airingStatus.forEach((s) => (params = params.append('airingStatus', s)));
    if (filters?.hasFile !== undefined) params = params.set('hasFile', filters.hasFile);
    if (filters?.codec?.length) filters.codec.forEach((c) => (params = params.append('codec', c)));
    if (filters?.mediaType?.length)
      filters.mediaType.forEach((t) => (params = params.append('mediaType', t)));
    if (filters?.ageRating?.length)
      filters.ageRating.forEach((r) => (params = params.append('ageRating', r)));
    if (enriched !== undefined) params = params.set('enriched', enriched);
    return this.http.get<PagedResponse<AnimeDetailsResponse>>(this.base, { params });
  }

  getAnime(id: string): Observable<AnimeDetailsResponse> {
    return this.http.get<AnimeDetailsResponse>(`${this.base}/${id}`);
  }

  getAnimeByMalId(malId: string): Observable<AnimeDetailsResponse> {
    return this.http.get<AnimeDetailsResponse>(`${this.base}/by-mal/${malId}`);
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

  syncAnimes(): Observable<void> {
    return this.http.post<void>(`${this.base}/sync`, null);
  }

  getCoverageGap(gapType: string): Observable<CoverageGapItem[]> {
    return this.http.get<CoverageGapItem[]>(`${this.base}/coverage-gap/${gapType}`);
  }

  getSeasonalAnimes(year: number, season: string): Observable<SeasonalAnimeItem[]> {
    const params = new HttpParams().set('year', year).set('season', season);
    return this.http.get<SeasonalAnimeItem[]>(this.seasonalBase, { params });
  }

  updateMalListStatus(malId: string, request: MalListStatusRequest, sourceAniDbId?: string | null): Observable<void> {
    const params = sourceAniDbId
      ? new HttpParams().set('sourceAniDbId', sourceAniDbId)
      : undefined;
    return this.http.put<void>(`${this.malBase}/list/${malId}`, request, { params });
  }

  refreshMalToken(): Observable<void> {
    return this.http.post<void>(`${this.malBase}/token/refresh`, null);
  }

  getFranchiseConstellation(malId: string): Observable<FranchiseConstellationData> {
    return this.http.get<FranchiseConstellationData>(`${this.malBase}/franchise/${malId}`);
  }

  searchMalAnime(query: string): Observable<MalSearchResult[]> {
    const params = new HttpParams().set('q', query);
    return this.http.get<MalSearchResult[]>(`${this.malBase}/search`, { params });
  }

  searchMalAnimeWithDetails(query: string, limit = 100): Observable<SeasonalAnimeItem[]> {
    const params = new HttpParams().set('q', query).set('limit', limit);
    return this.http.get<SeasonalAnimeItem[]>(`${this.malBase}/anime-search`, { params });
  }

  getCrossLinks(): Observable<CrossLinkEntry[]> {
    return this.http.get<CrossLinkEntry[]>(`${this.malBase}/crosslinks`);
  }

  removeCrossLink(sourceAniDbId: string, malId: string): Observable<void> {
    const params = new HttpParams()
      .set('sourceAniDbId', sourceAniDbId)
      .set('malId', malId);
    return this.http.delete<void>(`${this.malBase}/crosslink`, { params });
  }
}

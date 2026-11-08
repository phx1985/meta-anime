import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AnimeStatisticsResponse,
  ActivityStatsResponse,
  ActivityEpisodesResponse,
  ActivityPeriod,
  AnimeResponse,
  AnimeDetailsResponse,
  PagedResponse,
  AnimeFilters,
  CreateAnimeRequest,
  UpdateAnimeRequest,
  SeasonalAnimeItem,
  SeasonalSuggestionItem,
  SeasonalSuggestionScope,
  FranchiseConstellationData,
  CoverageGapItem,
  CrossLinkEntry,
  MalSearchResult,
  SeiyuuNexusResponse,
  VoiceActorProfileResponse, ActivityFeedResponse, ActivitySummaryResponse, ActivityConfigResponse,
  ActivityTrendResponse } from '../models/anime.models';
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

  getActivityStats(tzOffset: number): Observable<ActivityStatsResponse> {
    return this.http.get<ActivityStatsResponse>(`${this.base}/activity-stats`, {
      params: { tzOffset: tzOffset.toString() },
    });
  }

  refreshActivityStats(): Observable<void> {
    return this.http.post<void>(`${this.base}/activity-stats/refresh`, null);
  }

  /** Newest-first list of the episodes imported during the given activity period. */
  /** Activity Hub feed (adds, downloads, completions, sync diffs). */
  /** Activity polling cadence (0 = manual only). */
  getActivityConfig(): Observable<ActivityConfigResponse> {
    return this.http.get<ActivityConfigResponse>('/api/v1/activity/config');
  }

  /** Multi-series trend (ingested / added / watched / syncs) for the hub chart. */
  getActivityTrend(
    period: ActivityPeriod,
    tzOffset: number,
    year?: number | null,
  ): Observable<ActivityTrendResponse> {
    const params: Record<string, string> = { period, tzOffset: String(tzOffset) };
    if (period === 'thisYear' && year) params['year'] = String(year);

    return this.http.get<ActivityTrendResponse>('/api/v1/activity/trend', { params });
  }

  /** Enqueues the activity backfill now (first-run prompt on the hub). */
  refreshActivity(): Observable<void> {
    return this.http.post<void>('/api/v1/activity/refresh', {});
  }

  updateActivityConfig(intervalMinutes: number): Observable<ActivityConfigResponse> {
    return this.http.put<ActivityConfigResponse>('/api/v1/activity/config', { intervalMinutes });
  }

  /** Net KPI counters for the Activity hub (adds − removals, ingested, watched). */
  getActivitySummary(days = 30): Observable<ActivitySummaryResponse> {
    return this.http.get<ActivitySummaryResponse>('/api/v1/activity/summary', {
      params: { days: String(days) },
    });
  }

  /**
   * Same counters, but over the CALENDAR window of `period` — the boundary the trend chart and
   * the download tiles use, so the global range pills drive every card on the page.
   */
  getActivityPeriodSummary(
    period: ActivityPeriod,
    tzOffset: number,
    year?: number | null,
  ): Observable<ActivitySummaryResponse> {
    const params: Record<string, string> = { period, tzOffset: String(tzOffset) };
    if (period === 'thisYear' && year) params['year'] = String(year);

    return this.http.get<ActivitySummaryResponse>('/api/v1/activity/summary', { params });
  }

  /** Years the hub can chart (newest first) — drives the year picker next to "Year". */
  getActivityYears(): Observable<number[]> {
    return this.http.get<number[]>('/api/v1/activity/years');
  }

  getActivityFeed(
    type: string,
    page = 1,
    pageSize = 25,
    search = '',
    period?: ActivityPeriod,
    tzOffset = 0,
    year?: number | null,
  ): Observable<ActivityFeedResponse> {
    let params = new HttpParams().set('type', type).set('page', String(page)).set('pageSize', String(pageSize));
    if (search.trim()) params = params.set('search', search.trim());
    // The timeline follows the same range pills as the tiles and the chart.
    if (period) params = params.set('period', period).set('tzOffset', String(tzOffset));
    if (period === 'thisYear' && year) params = params.set('year', String(year));
    return this.http.get<ActivityFeedResponse>('/api/v1/activity/feed', { params });
  }

  getActivityEpisodes(
    period: ActivityPeriod,
    tzOffset: number,
    limit = 250,
  ): Observable<ActivityEpisodesResponse> {
    return this.http.get<ActivityEpisodesResponse>(`${this.base}/activity-stats/episodes`, {
      params: {
        period,
        tzOffset: tzOffset.toString(),
        limit: limit.toString(),
      },
    });
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

  /**
   * "For you" deck: the season ranked against the user's own MAL history.
   * Shares the backend's 15-minute seasonal cache — no extra MAL round-trip.
   */
  getSeasonalSuggestions(
    year: number,
    season: string,
    limit = 16,
    scope: SeasonalSuggestionScope = 'current',
    range?: { fromYear: number; fromSeason: string; toYear: number; toSeason: string },
    excludeChinese = false,
    excludeGenres: string[] = [],
  ): Observable<SeasonalSuggestionItem[]> {
    let params = new HttpParams()
      .set('year', year)
      .set('season', season)
      .set('limit', limit)
      .set('scope', scope);

    if (scope === 'custom' && range) {
      params = params
        .set('fromYear', range.fromYear)
        .set('fromSeason', range.fromSeason)
        .set('toYear', range.toYear)
        .set('toSeason', range.toSeason);
    }

    if (excludeChinese) params = params.set('excludeChinese', true);
    if (excludeGenres.length > 0) params = params.set('excludeGenres', excludeGenres.join(','));

    return this.http.get<SeasonalSuggestionItem[]>(`${this.seasonalBase}/suggestions`, { params });
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

  getSeiyuuNexus(malId: string): Observable<SeiyuuNexusResponse> {
    return this.http.get<SeiyuuNexusResponse>(`${this.base}/${malId}/seiyuu-nexus`);
  }

  getVoiceActorProfile(personId: number): Observable<VoiceActorProfileResponse> {
    return this.http.get<VoiceActorProfileResponse>(`${this.base}/va/${personId}/profile`);
  }
}

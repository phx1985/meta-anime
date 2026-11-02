import { Injectable, inject, signal } from '@angular/core';
import { Observable, of } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { AnimeService } from './anime.service';
import { UserPreferencesService } from './user-preferences.service';
import { ProviderStateService } from './provider-state.service';
import { AnimeDetailsResponse, AnimeFilters } from '../models/anime.models';

export type LuckyPickState = 'idle' | 'spinning' | 'result' | 'no-match' | 'error';
export type LuckySpinMode = 'open' | 'reroll';
export type LuckyFilterKey = 'planToWatch' | 'finished' | 'hasFiles';

/** How long the orb stays up. The result waits for the longer of (spin time, network). */
const SPIN_OPEN_MS = 1600;
const SPIN_REROLL_MS = 950;
/** Rolls kept in the session history strip. */
const HISTORY_LIMIT = 5;
/** How many times we re-sample when the random index/candidate was already seen. */
const MAX_ATTEMPTS = 4;

/**
 * Shared "I'm Feeling Lucky" engine.
 *
 * Owns the roll state machine, anti-repeat memory, session history and the background
 * prefetch so both launchers (the modal and the Bot-kun in-chat card) share one brain
 * instead of duplicating the pick logic.
 *
 * Pick contract (unchanged from the API's point of view):
 *   GET /animes?page=1&pageSize=1&<filters> → totalCount
 *   GET /animes?page=<random+1>&pageSize=1&<filters> → the anime
 */
@Injectable({ providedIn: 'root' })
export class LuckyPickService {
  private readonly animeService = inject(AnimeService);
  private readonly userPrefs = inject(UserPreferencesService);
  private readonly providerState = inject(ProviderStateService);

  readonly prefersReducedMotion =
    typeof window !== 'undefined' && typeof window.matchMedia === 'function'
      ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
      : false;

  private readonly _state = signal<LuckyPickState>('idle');
  readonly state = this._state.asReadonly();

  private readonly _result = signal<AnimeDetailsResponse | null>(null);
  readonly result = this._result.asReadonly();

  /** Last picks of this session, newest first, capped at HISTORY_LIMIT. */
  private readonly _history = signal<AnimeDetailsResponse[]>([]);
  readonly history = this._history.asReadonly();

  /** Indices already returned — cheap pre-check that costs no request. */
  private readonly seenIndices = new Set<number>();
  /** Real ids already returned — survives index drift after a library sync. */
  private readonly seenIds = new Set<string>();

  private prefetched: AnimeDetailsResponse | null = null;
  private prefetching = false;
  private timer: ReturnType<typeof setTimeout> | null = null;

  // ── Filters (the three switches also live in Settings → App Preferences) ────
  readonly planToWatch = this.userPrefs.luckyOnlyPlanToWatch;
  readonly onlyFinished = this.userPrefs.luckyOnlyFinished;
  readonly onlyWithFiles = this.userPrefs.luckyOnlyWithFiles;
  readonly sonarrEnabled = this.providerState.sonarrEnabled;

  /** Query filters derived from the current switches. */
  currentFilters(): AnimeFilters {
    const filters: AnimeFilters = {};
    if (this.planToWatch()) filters.status = ['plan_to_watch'];
    if (this.onlyFinished()) filters.airingStatus = ['finished_airing'];
    // Local-file records only exist when Sonarr is configured.
    if (this.onlyWithFiles() && this.sonarrEnabled()) filters.hasFile = true;
    return filters;
  }

  /** One-tap filter toggle from the modal header. Resets memory and re-rolls. */
  toggleFilter(key: LuckyFilterKey): void {
    if (key === 'planToWatch') this.userPrefs.setLuckyOnlyPlanToWatch(!this.planToWatch());
    else if (key === 'finished') this.userPrefs.setLuckyOnlyFinished(!this.onlyFinished());
    else this.userPrefs.setLuckyOnlyWithFiles(!this.onlyWithFiles());

    this.reset();
    this.roll('reroll');
  }

  /** Forget anti-repeat memory + the prefetched candidate (filters changed). */
  reset(): void {
    this.seenIndices.clear();
    this.seenIds.clear();
    this.prefetched = null;
  }

  // ── Rolling ────────────────────────────────────────────────────────────────
  /**
   * Roll a new pick.
   * @param mode 'open' uses the longer theatre spin, 'reroll' is snappy and reuses
   *             the prefetched candidate so the reveal is instant on the wire.
   */
  roll(mode: LuckySpinMode = 'open'): void {
    if (this._state() === 'spinning') return;

    this.clearTimer();
    this._state.set('spinning');
    this._result.set(null);

    const startedAt = performance.now();
    const minSpin = this.prefersReducedMotion ? 0 : mode === 'reroll' ? SPIN_REROLL_MS : SPIN_OPEN_MS;

    const reveal = (item: AnimeDetailsResponse | null, fallback: LuckyPickState): void => {
      const wait = Math.max(0, minSpin - (performance.now() - startedAt));
      this.clearTimer();
      this.timer = setTimeout(() => {
        this.timer = null;
        if (item) {
          this._result.set(item);
          this.pushHistory(item);
          this._state.set('result');
          this.prefetchNext();
        } else {
          this._state.set(fallback);
        }
      }, wait);
    };

    if (mode === 'reroll' && this.prefetched) {
      const item = this.prefetched;
      this.prefetched = null;
      reveal(item, 'result');
      return;
    }

    this.buildPick().subscribe({
      next: (item) => reveal(item, 'no-match'),
      error: () => reveal(null, 'error'),
    });
  }

  /** Show an earlier pick from the history strip without spinning. */
  showHistorical(item: AnimeDetailsResponse): void {
    if (this._state() === 'spinning') return;
    this.clearTimer();
    this._result.set(item);
    this._state.set('result');
  }

  /** One-shot pick for the Bot-kun chat card — never mutates the modal state. */
  pickOnce(): Observable<AnimeDetailsResponse | null> {
    return this.buildPick();
  }

  /** Called when the modal closes: cancels a pending reveal. */
  dismiss(): void {
    this.clearTimer();
  }

  // ── Internals ──────────────────────────────────────────────────────────────
  private buildPick(): Observable<AnimeDetailsResponse | null> {
    const filters = this.currentFilters();
    return this.animeService.getAnimes(1, 1, filters).pipe(
      switchMap((page) => {
        const total = page.totalCount;
        if (total === 0) return of(null);
        // Memory is meaningless once we've seen (almost) everything.
        if (this.seenIndices.size >= total) {
          this.seenIndices.clear();
          this.seenIds.clear();
        }
        return this.pickCandidate(filters, total, 0);
      }),
    );
  }

  private pickCandidate(
    filters: AnimeFilters,
    total: number,
    attempt: number,
  ): Observable<AnimeDetailsResponse | null> {
    let index = Math.floor(Math.random() * total);
    // Cheap pass: skip indices we know about without paying a request.
    for (let guard = 0; this.seenIndices.has(index) && this.seenIndices.size < total && guard < 12; guard++) {
      index = Math.floor(Math.random() * total);
    }

    // pageSize=1 → page N is item N
    return this.animeService.getAnimes(index + 1, 1, filters).pipe(
      switchMap((res) => {
        const item = res.data[0] ?? null;
        if (!item) return of(null);
        const id = String(item.aniDbId ?? item.title);
        // Index memory can drift (a sync inserts a title), so validate the real id too.
        if (this.seenIds.has(id) && total > 1 && attempt < MAX_ATTEMPTS) {
          return this.pickCandidate(filters, total, attempt + 1);
        }
        this.seenIndices.add(index);
        this.seenIds.add(id);
        return of(item);
      }),
    );
  }

  /** Warm the next reroll in the background so it reveals with zero network wait. */
  private prefetchNext(): void {
    if (this.prefetching || this.prefetched) return;
    this.prefetching = true;
    this.buildPick().subscribe({
      next: (item) => {
        this.prefetched = item;
        this.prefetching = false;
      },
      error: () => {
        this.prefetching = false;
      },
    });
  }

  private pushHistory(item: AnimeDetailsResponse): void {
    const id = String(item.aniDbId ?? item.title);
    this._history.update((list) => {
      const withoutDup = list.filter((x) => String(x.aniDbId ?? x.title) !== id);
      return [item, ...withoutDup].slice(0, HISTORY_LIMIT);
    });
  }

  private clearTimer(): void {
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}

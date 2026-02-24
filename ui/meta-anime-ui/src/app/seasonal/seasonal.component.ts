import {
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  inject,
  signal,
  computed,
  ViewChildren,
  QueryList,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { trigger, state, style, animate, transition, query, stagger, group } from '@angular/animations';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SettingsService } from '../services/settings.service';
import { SeasonalAnimeItem, SeasonName } from '../models/anime.models';
import { SeasonalCardComponent } from './seasonal-card/seasonal-card.component';
import { GENRE_LIST } from '../anime-list/anime-list.component';

const MIN_YEAR = 1963;

const NSFW_GENRES = ['Ecchi', 'Hentai', 'Erotica', 'Boys Love', 'Girls Love'];
const SFW_GENRE_LIST = GENRE_LIST.filter(g => !NSFW_GENRES.includes(g));

export interface SeasonPill {
  season: SeasonName;
  labelKey: string;
}

const ALL_SEASONS: SeasonPill[] = [
  { season: 'winter', labelKey: 'seasonal.season.winter' },
  { season: 'spring', labelKey: 'seasonal.season.spring' },
  { season: 'summer', labelKey: 'seasonal.season.summer' },
  { season: 'fall',   labelKey: 'seasonal.season.fall'   },
];

export const SEASONAL_MAL_STATES = [
  { value: 'not_in',        labelKey: 'seasonal.filter.malStatus.not_in' },
  { value: 'watching',      labelKey: 'animeCard.malStatus.watching' },
  { value: 'completed',     labelKey: 'animeCard.malStatus.completed' },
  { value: 'on_hold',       labelKey: 'animeCard.malStatus.on_hold' },
  { value: 'plan_to_watch', labelKey: 'animeCard.malStatus.plan_to_watch' },
  { value: 'dropped',       labelKey: 'animeCard.malStatus.dropped' },
];

export const SEASONAL_SORT_OPTIONS = [
  { value: 'title',     labelKey: 'seasonal.filter.sort.title' },
  { value: 'malscore',  labelKey: 'seasonal.filter.sort.malScore' },
  { value: 'startdate', labelKey: 'seasonal.filter.sort.startDate' },
];

/** Duration of the simultaneous leave (fall) animation (ms). */
const FLIP_LEAVE_MS      = 300;
/** Delay before FLIP runs = leave duration + small reflow buffer (ms). */
const FLIP_BASE_DELAY_MS = FLIP_LEAVE_MS + 50;
/** Duration of the FLIP slide transition (ms). */
const FLIP_DURATION_MS   = 380;

/** Returns the current season name based on the month (1–12). */
function getCurrentSeason(month: number): SeasonName {
  if (month <= 3) return 'winter';
  if (month <= 6) return 'spring';
  if (month <= 9) return 'summer';
  return 'fall';
}

@Component({
  selector: 'app-seasonal',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe, SeasonalCardComponent],
  templateUrl: './seasonal.component.html',
  styleUrl: './seasonal.component.scss',
  animations: [
    trigger('filterCollapse', [
      state('open',   style({ height: '*',    opacity: 1, overflow: 'hidden' })),
      state('closed', style({ height: '0px', opacity: 0, overflow: 'hidden' })),
      transition('open <=> closed', animate('320ms cubic-bezier(0.4, 0, 0.2, 1)')),
    ]),
    trigger('gridAnimation', [
      transition('* => *', [
        group([
          // All filtered-out cards fall at exactly the same time — no stagger.
          // Simultaneous fall keeps the total leave phase short (FLIP_LEAVE_MS)
          // so the remaining cards appear quickly instead of after a long gap.
          query(':leave', [
            animate(`${FLIP_LEAVE_MS}ms cubic-bezier(0.4, 0, 0.9, 0.6)`,
              style({ opacity: 0, transform: 'translateY(56px) scale(0.84)', filter: 'blur(3px)' })
            ),
          ], { optional: true }),
          // Pre-hide entering cards (blur + fade) while the fall plays.
          // The blur start-state makes the unblur reveal feel soft.
          query(':enter', [
            style({ opacity: 0, transform: 'translateY(-6px) scale(0.97)', filter: 'blur(8px)' }),
          ], { optional: true }),
        ]),
        // After the fall is done, entering cards unblur and rise in together.
        // A gentle stagger (12ms) avoids a single wall-of-cards pop-in.
        query(':enter', [
          stagger(12, [
            animate('360ms cubic-bezier(0.4, 0, 0.2, 1)',
              style({ opacity: 1, transform: 'none', filter: 'blur(0px)' })
            ),
          ]),
        ], { optional: true }),
      ]),
    ]),
  ],
})
export class SeasonalComponent {
  private animeService    = inject(AnimeService);
  private settingsService = inject(SettingsService);
  private destroyRef      = inject(DestroyRef);

  // ── FLIP animation ────────────────────────────────────────────────────────
  @ViewChildren('cell') private cellRefs!: QueryList<ElementRef<HTMLElement>>;
  private readonly _posSnapshot = new Map<number, DOMRect>();
  private _flipTimeoutId: ReturnType<typeof setTimeout> | null = null;

  /**
   * Captures the current rendered position of every grid cell, keyed by malId.
   * Must be called *before* any signal changes so Angular hasn't yet re-rendered.
   */
  private _snapshotPositions(): void {
    this._posSnapshot.clear();
    if (!this.cellRefs) return;
    this.cellRefs.forEach(ref => {
      const el = ref.nativeElement;
      const id = parseInt(el.dataset['malId'] ?? '', 10);
      if (!isNaN(id)) {
        // Hint the browser to promote this cell to a composite layer now,
        // so the GPU is ready before the FLIP transition starts.
        el.classList.add('flip-active');
        this._posSnapshot.set(id, el.getBoundingClientRect());
      }
    });
  }

  /**
   * Snapshot current positions then schedule a FLIP after Angular has re-rendered.
   * For pure sort changes (no cards leave) the FLIP runs in the very next rAF.
   * For filter changes some cards fall away first; the FLIP is delayed until the
   * staggered fall animation has finished so the grid has fully reflowed.
   */
  private _beginFilterChange(): void {
    if (this._flipTimeoutId !== null) {
      clearTimeout(this._flipTimeoutId);
      this._flipTimeoutId = null;
    }
    this._snapshotPositions();

    // Defer leave-count computation: signals update synchronously in the current
    // call stack so by the time this microtask runs filteredAnimes() is fresh.
    Promise.resolve().then(() => {
      const prevIds = new Set(this._posSnapshot.keys());
      const newIds  = new Set(this.filteredAnimes().map(a => a.malId));
      const leaving = [...prevIds].filter(id => !newIds.has(id.toString())).length;
      // Delay matches the leave-fall duration so FLIP fires after the grid reflows.
      const delay = leaving > 0 ? FLIP_BASE_DELAY_MS : 0;

      const run = () => requestAnimationFrame(() => this._applyFlip());
      if (delay > 0) {
        this._flipTimeoutId = setTimeout(run, delay);
      } else {
        run();
      }
    });
  }

  /**
   * FLIP — play phase.
   * For each cell that was rendered before AND after the filter change, compute
   * the translation delta (old grid position → new grid position) and apply a
   * smooth CSS transition so the card slides rather than snapping.
   * Newly entering cards (no snapshot entry) are left untouched — Angular's
   * @gridAnimation rise animation handles them.
   */
  private _applyFlip(): void {
    this._flipTimeoutId = null;
    if (this._posSnapshot.size === 0 || !this.cellRefs) return;

    this.cellRefs.forEach(ref => {
      const el = ref.nativeElement;
      const id = parseInt(el.dataset['malId'] ?? '', 10);
      const oldRect = this._posSnapshot.get(id);
      if (!oldRect) return; // `:enter` card — Angular animation handles it

      const newRect = el.getBoundingClientRect();
      const dx = oldRect.left - newRect.left;
      const dy = oldRect.top  - newRect.top;
      if (Math.abs(dx) < 1 && Math.abs(dy) < 1) {
        // Position unchanged — remove GPU hint; no transition needed.
        el.classList.remove('flip-active');
        return;
      }

      // Invert: jump the element back to its perceived old position (no transition).
      // The explicit `transition: none` ensures no easing runs during the jump.
      el.style.transition = 'none';
      el.style.transform  = `translate(${dx}px, ${dy}px)`;
      // Forcing a layout read (getBoundingClientRect) flushes pending style
      // recalculations so the browser commits the "transition: none" +
      // the translate transform BEFORE we switch the transition back on.
      // Without this flush the browser would batch the two style changes and
      // never see an intermediate state to animate FROM.
      el.getBoundingClientRect();

      // Play: animate from the old position to the natural new position.
      el.style.transition = `transform ${FLIP_DURATION_MS}ms cubic-bezier(0.25, 0.46, 0.45, 0.94)`;
      el.style.transform  = '';

      // Clean up inline styles once the transition finishes.
      // A timeout fallback ensures cleanup even if the transition is interrupted
      // (e.g. element removed from the DOM before transitionend fires).
      const safeCleanup = () => {
        el.style.transition = '';
        el.classList.remove('flip-active');
        el.removeEventListener('transitionend', safeCleanup);
        clearTimeout(fallbackId);
      };
      const fallbackId = setTimeout(safeCleanup, FLIP_DURATION_MS + 100);
      el.addEventListener('transitionend', safeCleanup);
    });

    this._posSnapshot.clear();
  }

  private readonly _now        = new Date();
  private readonly _maxYear    = this._now.getFullYear();
  private readonly _initSeason = getCurrentSeason(this._now.getMonth() + 1);

  // Year picker
  readonly allSeasons = ALL_SEASONS;
  readonly minYear    = MIN_YEAR;
  readonly maxYear    = this._maxYear;

  /** Array of years from minYear to maxYear (reversed: newest first). Used for <select>. */
  readonly yearOptions = Array.from(
    { length: this._maxYear - MIN_YEAR + 1 },
    (_, i) => this._maxYear - i
  );

  selectedYear   = signal(this._maxYear);
  selectedSeason = signal<SeasonName>(this._initSeason);

  readonly canGoPrev = computed(() => this.selectedYear() > MIN_YEAR);
  readonly canGoNext = computed(() => this.selectedYear() < this._maxYear);

  animes  = signal<SeasonalAnimeItem[]>([]);
  loading = signal(false);
  error   = signal(false);

  // ── NSFW ──────────────────────────────────────────────────────────────────
  showNsfw = signal(true);

  // ── Filter state ──────────────────────────────────────────────────────────
  filterOpen        = signal(true);
  genreDropdownOpen = false;

  filterGenre     = signal<string[]>([]);
  genreOperator   = signal<'or' | 'and'>('or');
  filterMalStatus = signal<string[]>([]);
  sortBy          = signal('title');
  sortOrder       = signal<'asc' | 'desc'>('asc');

  /** Incremented every time a filter/sort changes – drives the stagger animation. */
  filterVersion = signal(0);

  readonly malStates   = SEASONAL_MAL_STATES;
  readonly sortOptions = SEASONAL_SORT_OPTIONS;

  /** Genre list filtered by NSFW setting */
  readonly filteredGenreList = computed(() =>
    this.showNsfw() ? GENRE_LIST : SFW_GENRE_LIST
  );

  /** Label shown on the genre dropdown trigger button */
  readonly genreLabel = computed(() => {
    const sel = this.filterGenre();
    if (sel.length === 0) return null;
    if (sel.length <= 2) return sel.join(', ');
    return `${sel[0]}, ${sel[1]} +${sel.length - 2}`;
  });

  /** Count of active (non-default) filters for the badge */
  get activeFilterCount(): number {
    let n = 0;
    if (this.filterGenre().length) n++;
    if (this.filterMalStatus().length) n++;
    if (this.sortBy() !== 'title' || this.sortOrder() !== 'asc') n++;
    return n;
  }

  /** Filtered and sorted anime list – applied client-side from the already-loaded season data. */
  readonly filteredAnimes = computed(() => {
    let result = this.animes();

    // Genre filter
    const genres = this.filterGenre();
    if (genres.length > 0) {
      const isAnd = this.genreOperator() === 'and';
      result = result.filter(a => {
        const animeGenres = (a.genres ?? []).map(g => g.toLowerCase());
        return isAnd
          ? genres.every(g => animeGenres.includes(g.toLowerCase()))
          : genres.some(g => animeGenres.includes(g.toLowerCase()));
      });
    }

    // MAL status filter
    const malStatuses = this.filterMalStatus();
    if (malStatuses.length > 0) {
      result = result.filter(a =>
        malStatuses.some(s => {
          if (s === 'not_in') return !a.inMal;
          return a.malViewState === s;
        })
      );
    }

    // Sort
    const sb = this.sortBy();
    const asc = this.sortOrder() === 'asc';
    result = [...result].sort((a, b) => {
      let cmp = 0;
      if (sb === 'malscore') {
        cmp = (a.malScore ?? 0) - (b.malScore ?? 0);
      } else if (sb === 'startdate') {
        cmp = (a.startDate ?? '').localeCompare(b.startDate ?? '');
      } else {
        cmp = a.title.localeCompare(b.title);
      }
      return asc ? cmp : -cmp;
    });

    return result;
  });

  constructor() {
    this.settingsService.getShowNsfw()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(show => this.showNsfw.set(show));

    // Initial load
    this._loadCurrent();
  }

  // ── Year navigation ──────────────────────────────────────────────────────
  prevYear(): void {
    if (!this.canGoPrev()) return;
    this.selectedYear.update(y => y - 1);
    this.selectedSeason.set('winter');
    this._loadCurrent();
  }

  nextYear(): void {
    if (!this.canGoNext()) return;
    this.selectedYear.update(y => y + 1);
    this.selectedSeason.set('winter');
    this._loadCurrent();
  }

  setYear(year: number): void {
    const clamped = Math.max(MIN_YEAR, Math.min(this._maxYear, year));
    if (clamped === this.selectedYear()) return;
    this.selectedYear.set(clamped);
    this.selectedSeason.set('winter');
    this._loadCurrent();
  }

  // ── Season selection ─────────────────────────────────────────────────────
  selectSeason(season: SeasonName): void {
    if (this.selectedSeason() === season) return;
    this.selectedSeason.set(season);
    this._loadCurrent();
  }

  // ── Filter panel ─────────────────────────────────────────────────────────

  toggleFilterPanel(): void {
    this.filterOpen.update(v => !v);
  }

  // ── Genre dropdown ────────────────────────────────────────────────────────

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (this.genreDropdownOpen && !target.closest('.genre-dropdown')) {
      this.genreDropdownOpen = false;
    }
  }

  toggleGenreDropdown(event: MouseEvent): void {
    event.stopPropagation();
    this.genreDropdownOpen = !this.genreDropdownOpen;
  }

  toggleGenre(genre: string, event: MouseEvent): void {
    this._beginFilterChange();
    event.stopPropagation();
    this.filterGenre.update(v =>
      v.includes(genre) ? v.filter(g => g !== genre) : [...v, genre]
    );
    if (this.filterGenre().length <= 1) this.genreOperator.set('or');
    this.filterVersion.update(v => v + 1);
  }

  clearGenres(event: MouseEvent): void {
    this._beginFilterChange();
    event.stopPropagation();
    this.filterGenre.set([]);
    this.filterVersion.update(v => v + 1);
  }

  // ── MAL status pills ──────────────────────────────────────────────────────

  toggleMalStatus(status: string): void {
    this._beginFilterChange();
    this.filterMalStatus.update(v =>
      v.includes(status) ? v.filter(s => s !== status) : [...v, status]
    );
    this.filterVersion.update(v => v + 1);
  }

  // ── Sort ──────────────────────────────────────────────────────────────────

  setGenreOperator(op: 'or' | 'and', event: MouseEvent): void {
    this._beginFilterChange();
    event.stopPropagation();
    this.genreOperator.set(op);
    this.filterVersion.update(v => v + 1);
  }

  setSortBy(value: string): void {
    this._beginFilterChange();
    this.sortBy.set(value);
    this.filterVersion.update(v => v + 1);
  }

  toggleSortOrder(): void {
    this._beginFilterChange();
    this.sortOrder.update(o => o === 'asc' ? 'desc' : 'asc');
    this.filterVersion.update(v => v + 1);
  }

  // ── Reset ─────────────────────────────────────────────────────────────────

  resetFilters(): void {
    this._beginFilterChange();
    this.filterGenre.set([]);
    this.genreOperator.set('or');
    this.filterMalStatus.set([]);
    this.sortBy.set('title');
    this.sortOrder.set('asc');
    this.genreDropdownOpen = false;
    this.filterVersion.update(v => v + 1);
  }

  // ── Loading ──────────────────────────────────────────────────────────────
  private _loadCurrent(): void {
    const year   = this.selectedYear();
    const season = this.selectedSeason();

    this.animes.set([]);
    this.loading.set(true);
    this.error.set(false);

    this.animeService.getSeasonalAnimes(year, season)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => {
          this.animes.set(data);
          this.loading.set(false);
          this.filterVersion.update(v => v + 1);
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
        }
      });
  }
}


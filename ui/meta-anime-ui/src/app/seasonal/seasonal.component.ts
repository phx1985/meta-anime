import {
  Component,
  DestroyRef,
  HostListener,
  inject,
  signal,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { trigger, state, style, animate, transition, query, stagger } from '@angular/animations';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SettingsService } from '../services/settings.service';
import { FilterPresetService } from '../services/filter-preset.service';
import { SeasonalAnimeItem, SeasonalFilterPreset, SeasonalSavedFilterState, SeasonName } from '../models/anime.models';
import { SeasonalCardComponent } from './seasonal-card/seasonal-card.component';
import { GENRE_LIST, MEDIA_TYPE_OPTIONS } from '../anime-list/anime-list.component';

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
      state('open',   style({ height: '*',    opacity: 1, overflow: 'visible' })),
      state('closed', style({ height: '0px', opacity: 0, overflow: 'hidden'  })),
      // Force overflow:hidden during the transition so content doesn't bleed
      // while animating; once fully open the state settles to overflow:visible
      // so the genre dropdown panel can escape the card.
      transition('open => closed', [style({ overflow: 'hidden' }), animate('320ms cubic-bezier(0.4, 0, 0.2, 1)')]),
      transition('closed => open', [style({ overflow: 'hidden' }), animate('320ms cubic-bezier(0.4, 0, 0.2, 1)')]),
    ]),
    trigger('gridAnimation', [
      transition('* => *', [
        query(':enter', [
          style({ opacity: 0, transform: 'translateY(-6px) scale(0.97)', filter: 'blur(8px)' }),
          stagger(12, [
            animate('360ms cubic-bezier(0.4, 0, 0.2, 1)',
              style({ opacity: 1, transform: 'none', filter: 'blur(0px)' })
            )
          ])
        ], { optional: true })
      ])
    ]),
  ],
})
export class SeasonalComponent {
  private animeService      = inject(AnimeService);
  private settingsService   = inject(SettingsService);
  private presetService     = inject(FilterPresetService);
  private destroyRef        = inject(DestroyRef);
  private translateService  = inject(TranslateService);

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
  /** Elapsed time (ms) for the last successful seasonal data fetch. */
  fetchElapsedMs = signal<number | null>(null);
  private fetchStartTime = 0;

  // ── NSFW ──────────────────────────────────────────────────────────────────
  showNsfw = signal(true);

  // ── Filter state ──────────────────────────────────────────────────────────
  filterOpen        = signal(false);
  genreDropdownOpen = false;

  filterGenre     = signal<string[]>([]);
  genreOperator   = signal<'or' | 'and'>('or');
  filterMalStatus = signal<string[]>([]);
  filterMediaType = signal<string[]>([]);
  sortBy          = signal('title');
  sortOrder       = signal<'asc' | 'desc'>('asc');

  /** Displayed animes — populated asynchronously via _applyFilteredResults()
   *  so items are always :enter when they appear, giving a consistent blur-rise
   *  animation on first load AND on every filter/sort/preset change. */
  displayedAnimes = signal<SeasonalAnimeItem[]>([]);
  /** Guard for rapid filter changes — only the last change animates in. */
  private _filterAnimTimeout: ReturnType<typeof setTimeout> | null = null;

  // ── Preset state ──────────────────────────────────────────────────────────
  savedPresets    = signal<SeasonalFilterPreset[]>([]);
  showSavePreset  = false;
  newPresetName   = '';

  readonly malStates         = SEASONAL_MAL_STATES;
  readonly sortOptions       = SEASONAL_SORT_OPTIONS;
  readonly mediaTypeOptions  = MEDIA_TYPE_OPTIONS;

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
    if (this.filterMediaType().length) n++;
    if (this.sortBy() !== 'title' || this.sortOrder() !== 'asc') n++;
    return n;
  }

  /** Short human-readable summary of current filters for the collapsed header */
  get filterSummary(): string {
    const parts: string[] = [];
    const genres = this.filterGenre();
    if (genres.length) {
      const sep = genres.length > 1 ? ` ${this.genreOperator().toUpperCase()} ` : '';
      parts.push(genres.join(sep));
    }
    const malStatuses = this.filterMalStatus();
    if (malStatuses.length) {
      parts.push(malStatuses
        .map(s => this.translateService.instant(
          s === 'not_in' ? 'seasonal.filter.malStatus.not_in' : `animeCard.malStatus.${s}`
        ))
        .join(', '));
    }
    const mediaTypes = this.filterMediaType();
    if (mediaTypes.length) {
      parts.push(mediaTypes
        .map(t => this.translateService.instant(`animeCard.mediaType.${t}`))
        .join(', '));
    }
    const isDefaultSort = this.sortBy() === 'title' && this.sortOrder() === 'asc';
    if (!isDefaultSort) {
      const sortOpt = SEASONAL_SORT_OPTIONS.find(o => o.value === this.sortBy());
      const sortLabel = sortOpt ? this.translateService.instant(sortOpt.labelKey) : this.sortBy();
      parts.push(`${sortLabel} ${this.sortOrder() === 'asc' ? '↑' : '↓'}`);
    }
    return parts.length === 0
      ? this.translateService.instant('seasonal.filter.noFilterActive')
      : parts.join(' · ');
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
    // Design:
    //  • 'not_in'  pill → matches animes NOT in the user's MAL list (inMal = false)
    //  • status pills  → matches animes IN the user's MAL list with a specific view state
    //
    // The two groups are mutually exclusive (an anime can't both be "not in MAL" and
    // have a MAL status). `toggleMalStatus` enforces this invariant in the UI so both
    // groups are never active at the same time. The filter below reflects that:
    const malStatuses = this.filterMalStatus();
    if (malStatuses.length > 0) {
      const notInSelected = malStatuses.includes('not_in');
      const statusSelected = malStatuses.filter(s => s !== 'not_in');

      result = result.filter(a => {
        // ── Not-in-MAL branch ──────────────────────────────────────────────
        if (!a.inMal) return notInSelected;

        // ── In-MAL branch ─────────────────────────────────────────────────
        if (statusSelected.length === 0) return false; // only 'not_in' active

        // If the anime is in MAL but malViewState is missing (cache miss /
        // data gap), keep it visible rather than hiding it silently.
        if (!a.malViewState) return true;

        return statusSelected.some(s => a.malViewState === s);
      });
    }

    // Media type filter
    const mediaTypes = this.filterMediaType();
    if (mediaTypes.length > 0) {
      result = result.filter(a =>
        mediaTypes.some(t => (a.mediaType ?? '').toLowerCase() === t.toLowerCase())
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

    this.savedPresets.set(this.presetService.getSeasonalPresets());

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
    event.stopPropagation();
    this.filterGenre.update(v =>
      v.includes(genre) ? v.filter(g => g !== genre) : [...v, genre]
    );
    if (this.filterGenre().length <= 1) this.genreOperator.set('or');
    this._applyFilteredResults();
  }

  clearGenres(event: MouseEvent): void {
    event.stopPropagation();
    this.filterGenre.set([]);
    this._applyFilteredResults();
  }

  // ── MAL status pills ──────────────────────────────────────────────────────

  toggleMalStatus(status: string): void {
    this.filterMalStatus.update(current => {
      if (current.includes(status)) {
        // Deselect: remove this pill
        return current.filter(s => s !== status);
      }
      // Select: enforce mutual exclusivity between 'not_in' and the status pills.
      //  • Selecting 'not_in'  → clear all status pills first
      //  • Selecting a status  → clear 'not_in' first
      if (status === 'not_in') {
        return ['not_in'];
      }
      return [...current.filter(s => s !== 'not_in'), status];
    });
    this._applyFilteredResults();
  }

  // ── Media type pills ──────────────────────────────────────────────────────

  toggleMediaType(type: string): void {
    this.filterMediaType.update(current =>
      current.includes(type) ? current.filter(t => t !== type) : [...current, type]
    );
    this._applyFilteredResults();
  }

  // ── Sort ──────────────────────────────────────────────────────────────────

  setGenreOperator(op: 'or' | 'and', event: MouseEvent): void {
    event.stopPropagation();
    this.genreOperator.set(op);
    this._applyFilteredResults();
  }

  setSortBy(value: string): void {
    this.sortBy.set(value);
    this._applyFilteredResults();
  }

  /** Returns true when this MAL status pill should be visually muted (its group is excluded). */
  isMalStatusMuted(status: string): boolean {
    const active = this.filterMalStatus();
    if (active.length === 0 || active.includes(status)) return false;
    // 'not_in' is muted when any real status is active, and vice versa
    if (status === 'not_in') return active.some(s => s !== 'not_in');
    return active.includes('not_in');
  }

  toggleSortOrder(): void {
    this.sortOrder.update(o => o === 'asc' ? 'desc' : 'asc');
    this._applyFilteredResults();
  }

  // ── Reset ─────────────────────────────────────────────────────────────────

  resetFilters(): void {
    this.filterGenre.set([]);
    this.genreOperator.set('or');
    this.filterMalStatus.set([]);
    this.filterMediaType.set([]);
    this.sortBy.set('title');
    this.sortOrder.set('asc');
    this.genreDropdownOpen = false;
    this._applyFilteredResults();
  }

  // ── Preset methods ────────────────────────────────────────────────────────

  applyPreset(preset: SeasonalFilterPreset): void {
    const f = preset.filters;
    this.filterGenre.set(f.genre          ?? []);
    this.genreOperator.set(f.genreOperator ?? 'or');
    this.filterMalStatus.set(f.malStatus  ?? []);
    this.filterMediaType.set(f.mediaType  ?? []);
    this.sortBy.set(f.sortBy              ?? 'title');
    this.sortOrder.set(f.sortOrder        ?? 'asc');
    this.genreDropdownOpen = false;
    this._applyFilteredResults();
    this.showSavePreset = false;
    this.filterOpen.set(false);
  }

  openSavePreset(): void {
    this.showSavePreset = !this.showSavePreset;
    if (this.showSavePreset) this.newPresetName = '';
  }

  confirmSavePreset(): void {
    const name = this.newPresetName.trim();
    if (!name) return;
    const preset: SeasonalFilterPreset = {
      id: `seasonal-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
      label: name,
      filters: this._snapshotFilterState(),
    };
    this.presetService.saveSeasonalPreset(preset);
    this.savedPresets.set(this.presetService.getSeasonalPresets());
    this.showSavePreset = false;
    this.newPresetName  = '';
  }

  cancelSavePreset(): void {
    this.showSavePreset = false;
    this.newPresetName  = '';
  }

  deletePreset(id: string, event: MouseEvent): void {
    event.stopPropagation();
    this.presetService.deleteSeasonalPreset(id);
    this.savedPresets.set(this.presetService.getSeasonalPresets());
  }

  private _snapshotFilterState(): SeasonalSavedFilterState {
    return {
      genre:         this.filterGenre().length      ? this.filterGenre()      : undefined,
      genreOperator: this.filterGenre().length > 1  ? this.genreOperator()    : undefined,
      malStatus:     this.filterMalStatus().length  ? this.filterMalStatus()  : undefined,
      mediaType:     this.filterMediaType().length  ? this.filterMediaType()  : undefined,
      sortBy:        this.sortBy()    !== 'title'   ? this.sortBy()           : undefined,
      sortOrder:     this.sortOrder() !== 'asc'     ? this.sortOrder()        : undefined,
    };
  }

  // ── Animation helper ─────────────────────────────────────────────────────
  /** Clear the grid instantly and repopulate it in the next event-loop tick.
   *  Because all items are removed from the DOM and re-added, Angular marks
   *  every card as :enter — so query(':enter') in @gridAnimation always fires
   *  the blur-rise stagger, regardless of whether the items were there before.
   *  A clearTimeout guard prevents rapid changes from causing multiple flashes. */
  private _applyFilteredResults(): void {
    if (this._filterAnimTimeout !== null) {
      clearTimeout(this._filterAnimTimeout);
    }
    this.displayedAnimes.set([]);
    this._filterAnimTimeout = setTimeout(() => {
      this._filterAnimTimeout = null;
      this.displayedAnimes.set(this.filteredAnimes());
    }, 0);
  }

  // ── Loading ──────────────────────────────────────────────────────────────
  private _loadCurrent(): void {
    const year   = this.selectedYear();
    const season = this.selectedSeason();

    this.error.set(false);
    this.fetchElapsedMs.set(null);

    // On the very first load (no data yet) show the loading spinner.
    // For subsequent season/year changes keep the existing animes visible so
    // the @gridAnimation blur-rise plays — making the animation homogeneous
    // between filter changes and season changes.
    if (this.animes().length === 0) {
      this.loading.set(true);
    }

    this.fetchStartTime = Date.now();
    this.animeService.getSeasonalAnimes(year, season)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => {
          this.animes.set(data);
          this.loading.set(false);
          this.fetchElapsedMs.set(Date.now() - this.fetchStartTime);
          this._applyFilteredResults();
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
          this.animes.set([]);           // clear stale data on error so empty state shows
          this.displayedAnimes.set([]);  // clear displayed grid immediately
        }
      });
  }
}


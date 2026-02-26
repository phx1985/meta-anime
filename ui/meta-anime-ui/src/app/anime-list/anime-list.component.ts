import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  QueryList,
  ViewChildren,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { trigger, state, style, animate, transition, query, stagger } from '@angular/animations';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SettingsService } from '../services/settings.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { FilterPresetService } from '../services/filter-preset.service';
import { UserPreferencesService } from '../services/user-preferences.service';
import {
  AnimeDetailsResponse,
  AnimeFilters,
  FilterPreset,
  SavedFilterState,
} from '../models/anime.models';
import { AnimeCardComponent } from './anime-card/anime-card.component';
import { StorageKeys } from '../constants/storage-keys';

const PAGE_SIZE = 20;
const DISPLAY_MODE_KEY = StorageKeys.displayMode;

export type DisplayMode = 1 | 2 | 3;

export { GENRE_LIST, THEME_LIST, DEMOGRAPHIC_LIST } from '../constants/genre-lists';
import {
  GENRE_LIST,
  THEME_LIST,
  DEMOGRAPHIC_LIST,
  SFW_GENRE_LIST,
  SFW_THEME_LIST,
  NSFW_GENRES,
  NSFW_THEMES,
} from '../constants/genre-lists';

export const WATCHING_STATES = [
  { value: 'watching', labelKey: 'animeCard.malStatus.watching' },
  { value: 'completed', labelKey: 'animeCard.malStatus.completed' },
  { value: 'on_hold', labelKey: 'animeCard.malStatus.on_hold' },
  { value: 'dropped', labelKey: 'animeCard.malStatus.dropped' },
  { value: 'plan_to_watch', labelKey: 'animeCard.malStatus.plan_to_watch' },
];

export const AIRING_STATES = [
  { value: 'finished_airing', labelKey: 'animeCard.seriesStatus.finished_airing' },
  { value: 'currently_airing', labelKey: 'animeCard.seriesStatus.currently_airing' },
  { value: 'not_yet_aired', labelKey: 'animeCard.seriesStatus.not_yet_aired' },
];

export const SORT_OPTIONS = [
  { value: 'title', labelKey: 'animeList.sort.title' },
  { value: 'malscore', labelKey: 'animeList.sort.malScore' },
  { value: 'myscore', labelKey: 'animeList.sort.myScore' },
  { value: 'startdate', labelKey: 'animeList.sort.startDate' },
  { value: 'enddate', labelKey: 'animeList.sort.endDate' },
  { value: 'addeddate', labelKey: 'animeList.sort.addedDate' },
  { value: 'sizeondisk', labelKey: 'animeList.sort.sizeOnDisk' },
  { value: 'totalsizedisk', labelKey: 'animeList.sort.totalSizeDisk' },
];

export const CODEC_OPTIONS = [
  { value: 'hevc', labelKey: 'animeList.filter.codecs.hevc' },
  { value: 'h264', labelKey: 'animeList.filter.codecs.h264' },
  { value: 'av1', labelKey: 'animeList.filter.codecs.av1' },
  { value: 'mpeg2', labelKey: 'animeList.filter.codecs.mpeg2' },
];

export const MEDIA_TYPE_OPTIONS = [
  { value: 'tv', labelKey: 'animeCard.mediaType.tv' },
  { value: 'movie', labelKey: 'animeCard.mediaType.movie' },
  { value: 'ova', labelKey: 'animeCard.mediaType.ova' },
  { value: 'ona', labelKey: 'animeCard.mediaType.ona' },
  { value: 'special', labelKey: 'animeCard.mediaType.special' },
  { value: 'music', labelKey: 'animeCard.mediaType.music' },
];

@Component({
  selector: 'app-anime-list',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe, AnimeCardComponent],
  templateUrl: './anime-list.component.html',
  styleUrl: './anime-list.component.scss',
  animations: [
    trigger('gridLayout', [
      transition(':increment', [
        query(
          '.anime-card',
          [
            style({ opacity: 0.7, transform: 'translateY(6px) scale(0.97)' }),
            stagger(5, [
              animate(
                '300ms cubic-bezier(0.34, 1.4, 0.64, 1)',
                style({ opacity: 1, transform: 'none' }),
              ),
            ]),
          ],
          { optional: true },
        ),
      ]),
    ]),
    trigger('filterCollapse', [
      state('open', style({ height: '*', opacity: 1, overflow: 'visible' })),
      state('closed', style({ height: '0px', opacity: 0, overflow: 'hidden' })),
      // Force overflow:hidden during the transition so content doesn't bleed
      // while height animates; once fully open the state settles to overflow:visible
      // so the genre dropdown panel can escape the card.
      transition('open => closed', [
        style({ overflow: 'hidden' }),
        animate('320ms cubic-bezier(0.4, 0, 0.2, 1)'),
      ]),
      transition('closed => open', [
        style({ overflow: 'hidden' }),
        animate('320ms cubic-bezier(0.4, 0, 0.2, 1)'),
      ]),
    ]),
  ],
})
export class AnimeListComponent implements AfterViewInit, OnDestroy {
  private animeService = inject(AnimeService);
  private settingsService = inject(SettingsService);
  private syncSignalR = inject(SyncSignalRService);
  private destroyRef = inject(DestroyRef);
  private translateService = inject(TranslateService);
  readonly presetService = inject(FilterPresetService);
  readonly userPrefs = inject(UserPreferencesService);

  @ViewChildren('animeCard', { read: ElementRef }) cardRefs!: QueryList<ElementRef>;

  animes = signal<AnimeDetailsResponse[]>([]);
  loading = signal(false);
  loadingMore = signal(false);
  error = signal(false);
  totalCount = signal(0);
  allLoaded = signal(false);
  displayMode = signal<DisplayMode>(2);
  layoutState = signal(0);
  searched = signal(false);
  showNsfw = signal(true);
  /** Elapsed ms for the last search request (null until first search completes) */
  searchElapsedMs = signal<number | null>(null);

  /** Whether the filter body is expanded */
  filterOpen = signal(true);

  /** Whether the advanced filter sub-section is expanded */
  advancedOpen = signal(false);

  /** Whether the genre dropdown is open */
  genreDropdownOpen = false;

  /** Whether the theme dropdown is open */
  themeDropdownOpen = false;

  /** Whether the demographic dropdown is open */
  demographicDropdownOpen = false;

  /** Search text inside each filter popup */
  genreSearch = '';
  themeSearch = '';
  demographicSearch = '';

  // ── Preset state ──────────────────────────────────────────────────────────
  savedPresets = signal<FilterPreset[]>([]);
  /** Whether the "save as preset" inline form is visible */
  showSavePreset = false;
  newPresetName = '';

  // ── Filter state ──────────────────────────────────────────────────────────
  filterTitle = '';
  /** Signal so that computed() consumers (genreLabel, filteredGenreList) react to changes */
  filterGenre = signal<string[]>([]);
  genreOperator: 'or' | 'and' = 'or';
  /** Signal so that computed() consumers (themeLabel, filteredThemeList) react to changes */
  filterTheme = signal<string[]>([]);
  themeOperator: 'or' | 'and' = 'or';
  /** Signal so that computed() consumers (demographicLabel) react to changes */
  filterDemographic = signal<string[]>([]);
  demographicOperator: 'or' | 'and' = 'or';
  filterWatchState: string[] = [];
  filterAiringStatus: string[] = [];
  filterHasFile: 'any' | 'yes' | 'no' = 'any';
  filterCodec: string[] = [];
  filterMediaType: string[] = [];
  sortBy = 'title';
  sortOrder: 'asc' | 'desc' = 'asc';

  readonly watchingStates = WATCHING_STATES;
  readonly airingStates = AIRING_STATES;
  readonly sortOptions = SORT_OPTIONS;
  readonly codecOptions = CODEC_OPTIONS;
  readonly mediaTypeOptions = MEDIA_TYPE_OPTIONS;

  /** Genre list filtered by NSFW setting */
  readonly filteredGenreList = computed(() => (this.showNsfw() ? GENRE_LIST : SFW_GENRE_LIST));

  /** Theme list filtered by NSFW setting */
  readonly filteredThemeList = computed(() => (this.showNsfw() ? THEME_LIST : SFW_THEME_LIST));

  /** Label shown on the genre dropdown trigger button */
  readonly genreLabel = computed(() => {
    this.langSignal();
    const sel = this.filterGenre();
    if (sel.length === 0) return null;
    const t = (k: string) => this.translateService.instant(`genres.${k}`);
    if (sel.length <= 2) return sel.map(t).join(', ');
    return `${t(sel[0])}, ${t(sel[1])} +${sel.length - 2}`;
  });

  /** Label shown on the theme dropdown trigger button */
  readonly themeLabel = computed(() => {
    this.langSignal();
    const sel = this.filterTheme();
    if (sel.length === 0) return null;
    const t = (k: string) => this.translateService.instant(`genres.${k}`);
    if (sel.length <= 2) return sel.map(t).join(', ');
    return `${t(sel[0])}, ${t(sel[1])} +${sel.length - 2}`;
  });

  /** Label shown on the demographic dropdown trigger button */
  readonly demographicLabel = computed(() => {
    this.langSignal();
    const sel = this.filterDemographic();
    if (sel.length === 0) return null;
    const t = (k: string) => this.translateService.instant(`genres.${k}`);
    if (sel.length <= 2) return sel.map(t).join(', ');
    return `${t(sel[0])}, ${t(sel[1])} +${sel.length - 2}`;
  });

  readonly demographicOptions = DEMOGRAPHIC_LIST;

  /** Reactive language tag — changes when the user switches language. */
  private readonly langSignal = signal(
    this.translateService.currentLang || this.translateService.defaultLang || 'en',
  );

  /** Genre list: NSFW-filtered + sorted alphabetically (NSFW items last). */
  readonly sortedGenreList = computed(() => {
    this.langSignal();
    return (this.showNsfw() ? GENRE_LIST : SFW_GENRE_LIST)
      .map((k) => ({
        key: k,
        label: this.translateService.instant(`genres.${k}`),
        nsfw: NSFW_GENRES.includes(k),
      }))
      .sort((a, b) => (a.nsfw !== b.nsfw ? (a.nsfw ? 1 : -1) : a.label.localeCompare(b.label)));
  });

  /** Theme list: NSFW-filtered + sorted alphabetically (NSFW items last). */
  readonly sortedThemeList = computed(() => {
    this.langSignal();
    return (this.showNsfw() ? THEME_LIST : SFW_THEME_LIST)
      .map((k) => ({
        key: k,
        label: this.translateService.instant(`genres.${k}`),
        nsfw: NSFW_THEMES.includes(k),
      }))
      .sort((a, b) => (a.nsfw !== b.nsfw ? (a.nsfw ? 1 : -1) : a.label.localeCompare(b.label)));
  });

  /** Demographic list: sorted alphabetically by translated label. */
  readonly sortedDemographicList = computed(() => {
    this.langSignal();
    return DEMOGRAPHIC_LIST.map((k) => ({
      key: k,
      label: this.translateService.instant(`genres.${k}`),
    })).sort((a, b) => a.label.localeCompare(b.label));
  });

  /** Count of active (non-default) filters for the badge */
  get activeFilterCount(): number {
    let n = 0;
    if (this.filterTitle) n++;
    if (this.filterGenre().length) n++;
    if (this.filterTheme().length) n++;
    if (this.filterDemographic().length) n++;
    if (this.filterWatchState.length) n++;
    if (this.filterAiringStatus.length) n++;
    if (this.filterHasFile !== 'any') n++;
    if (this.filterCodec.length) n++;
    if (this.filterMediaType.length) n++;
    return n;
  }

  /** Count of active advanced filters (drives the badge on the Advanced toggle) */
  get advancedFilterCount(): number {
    let n = 0;
    if (this.filterGenre().length) n++;
    if (this.filterTheme().length) n++;
    if (this.filterDemographic().length) n++;
    if (this.filterWatchState.length) n++;
    if (this.filterAiringStatus.length) n++;
    if (this.filterHasFile !== 'any') n++;
    if (this.filterCodec.length) n++;
    if (this.filterMediaType.length) n++;
    if (this.sortBy !== 'title' || this.sortOrder !== 'asc') n++;
    return n;
  }

  private currentPage = 1;
  private scrollObserver: IntersectionObserver | null = null;
  private searchStartTime = 0;

  /** Short human-readable summary of the current filter + sort state for the card header */
  get filterSummary(): string {
    const parts: string[] = [];
    if (this.filterTitle) parts.push(`«${this.filterTitle}»`);
    const genres = this.filterGenre();
    if (genres.length) {
      const sep = genres.length > 1 ? ` ${this.genreOperator.toUpperCase()} ` : '';
      parts.push(genres.map((k) => this.translateService.instant(`genres.${k}`)).join(sep));
    }
    const themes = this.filterTheme();
    if (themes.length) {
      const sep = themes.length > 1 ? ` ${this.themeOperator.toUpperCase()} ` : '';
      parts.push(themes.map((k) => this.translateService.instant(`genres.${k}`)).join(sep));
    }
    const demographics = this.filterDemographic();
    if (demographics.length) {
      const sep = demographics.length > 1 ? ` ${this.demographicOperator.toUpperCase()} ` : '';
      parts.push(demographics.map((k) => this.translateService.instant(`genres.${k}`)).join(sep));
    }
    if (this.filterWatchState.length) {
      parts.push(
        this.filterWatchState
          .map((v) => this.translateService.instant(`animeCard.malStatus.${v}`))
          .join(', '),
      );
    }
    if (this.filterAiringStatus.length) {
      parts.push(
        this.filterAiringStatus
          .map((v) => this.translateService.instant(`animeCard.seriesStatus.${v}`))
          .join(', '),
      );
    }
    if (this.filterHasFile !== 'any') {
      parts.push(
        this.translateService.instant(
          this.filterHasFile === 'yes'
            ? 'animeList.filter.hasFileYes'
            : 'animeList.filter.hasFileNo',
        ),
      );
    }
    if (this.filterCodec.length) {
      parts.push(
        this.filterCodec
          .map((v) => {
            const opt = CODEC_OPTIONS.find((c) => c.value === v);
            return opt ? this.translateService.instant(opt.labelKey) : v;
          })
          .join(', '),
      );
    }
    if (this.filterMediaType.length) {
      parts.push(
        this.filterMediaType
          .map((v) => this.translateService.instant(`animeCard.mediaType.${v}`))
          .join(', '),
      );
    }
    const isDefaultSort = this.sortBy === 'title' && this.sortOrder === 'asc';
    if (!isDefaultSort) {
      const sortOpt = SORT_OPTIONS.find((o) => o.value === this.sortBy);
      const sortLabel = sortOpt ? this.translateService.instant(sortOpt.labelKey) : this.sortBy;
      parts.push(`${sortLabel} ${this.sortOrder === 'asc' ? '↑' : '↓'}`);
    }
    return parts.length === 0
      ? this.translateService.instant('animeList.filter.noFilterActive')
      : parts.join(' · ');
  }

  constructor() {
    const saved = localStorage.getItem(DISPLAY_MODE_KEY);
    const n = Number(saved);
    if (saved && (n === 1 || n === 2 || n === 3)) this.displayMode.set(n as DisplayMode);

    this.savedPresets.set(this.presetService.getSavedPresets());

    this.settingsService
      .getShowNsfw()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((show) => this.showNsfw.set(show));

    this.translateService.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((e) => this.langSignal.set(e.lang));

    this.syncSignalR.syncCompleted$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (this.searched()) this.loadAnimes(true);
    });

    // Restore and auto-apply the last used filters when the setting is enabled.
    if (this.userPrefs.autoApplyLastFilters()) {
      const raw = this.userPrefs.loadLastFilters();
      if (raw) {
        try {
          const restored = JSON.parse(raw) as SavedFilterState;
          this.applyFilterState(restored);
          this.search();
        } catch {
          // Corrupt storage entry — silently discard.
        }
      }
    }
  }

  ngAfterViewInit(): void {
    this.cardRefs.changes
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.setupScrollSentinel());
  }

  ngOnDestroy(): void {
    this.scrollObserver?.disconnect();
  }

  // ── Collapse outside click handler ────────────────────────────────────────

  // Popup close-on-backdrop handled inline in template; nothing to close via document:click.

  // ── Display mode ──────────────────────────────────────────────────────────

  setDisplayMode(mode: DisplayMode): void {
    if (this.displayMode() === mode) return;
    this.displayMode.set(mode);
    this.layoutState.update((v) => v + 1);
    localStorage.setItem(DISPLAY_MODE_KEY, String(mode));
  }

  // ── Filter panel collapse ─────────────────────────────────────────────────

  toggleFilterPanel(): void {
    this.filterOpen.update((v) => !v);
  }

  // ── Advanced section collapse ─────────────────────────────────────────────

  toggleAdvanced(): void {
    this.advancedOpen.update((v) => !v);
  }

  // ── Sort order toggle ─────────────────────────────────────────────────────

  toggleSortOrder(): void {
    this.sortOrder = this.sortOrder === 'asc' ? 'desc' : 'asc';
    if (this.searched()) this.loadAnimes(true);
  }

  onSortByChange(): void {
    if (this.searched()) this.loadAnimes(true);
  }

  // ── Pill multi-select toggle ──────────────────────────────────────────────

  togglePill(current: string[], value: string): string[] {
    return current.includes(value) ? current.filter((v) => v !== value) : [...current, value];
  }

  // ── Genre dropdown ────────────────────────────────────────────────────────

  toggleGenreDropdown(event: MouseEvent): void {
    event.stopPropagation(); // prevent document:click from immediately closing
    this.genreDropdownOpen = !this.genreDropdownOpen;
    if (this.genreDropdownOpen) {
      this.themeDropdownOpen = false;
      this.demographicDropdownOpen = false;
    }
  }

  toggleGenre(genre: string, event: MouseEvent): void {
    event.stopPropagation();
    this.filterGenre.update((v) => this.togglePill(v, genre));
    // Auto-reset operator to OR when ≤ 1 genre selected (operator toggle is hidden)
    if (this.filterGenre().length <= 1) this.genreOperator = 'or';
  }

  clearGenres(event: MouseEvent): void {
    event.stopPropagation();
    this.filterGenre.set([]);
  }

  // ── Theme dropdown ────────────────────────────────────────────────────────

  toggleThemeDropdown(event: MouseEvent): void {
    event.stopPropagation();
    this.themeDropdownOpen = !this.themeDropdownOpen;
    if (this.themeDropdownOpen) {
      this.genreDropdownOpen = false;
      this.demographicDropdownOpen = false;
    }
  }

  toggleTheme(theme: string, event: MouseEvent): void {
    event.stopPropagation();
    this.filterTheme.update((v) => this.togglePill(v, theme));
    if (this.filterTheme().length <= 1) this.themeOperator = 'or';
  }

  clearThemes(event: MouseEvent): void {
    event.stopPropagation();
    this.filterTheme.set([]);
  }

  // ── Demographic dropdown ──────────────────────────────────────────────────

  toggleDemographicDropdown(event: MouseEvent): void {
    event.stopPropagation();
    this.demographicDropdownOpen = !this.demographicDropdownOpen;
    if (this.demographicDropdownOpen) {
      this.genreDropdownOpen = false;
      this.themeDropdownOpen = false;
    }
  }

  toggleDemographic(demographic: string, event: MouseEvent): void {
    event.stopPropagation();
    this.filterDemographic.update((v) => this.togglePill(v, demographic));
    if (this.filterDemographic().length <= 1) this.demographicOperator = 'or';
  }

  clearDemographics(event: MouseEvent): void {
    event.stopPropagation();
    this.filterDemographic.set([]);
  }

  // ── Search / Reset ────────────────────────────────────────────────────────

  search(): void {
    this.userPrefs.saveLastFilters(JSON.stringify(this.snapshotFilters()));
    this.searched.set(true);
    this.filterOpen.set(false); // collapse filter panel on search
    this.genreDropdownOpen = false;
    this.themeDropdownOpen = false;
    this.demographicDropdownOpen = false;
    this.loadAnimes(true);
  }

  reset(): void {
    this.filterTitle = '';
    this.filterGenre.set([]);
    this.genreOperator = 'or';
    this.filterTheme.set([]);
    this.themeOperator = 'or';
    this.filterDemographic.set([]);
    this.demographicOperator = 'or';
    this.filterWatchState = [];
    this.filterAiringStatus = [];
    this.filterHasFile = 'any';
    this.filterCodec = [];
    this.filterMediaType = [];
    this.sortBy = 'title';
    this.sortOrder = 'asc';
    this.searched.set(false);
    this.filterOpen.set(true);
    this.advancedOpen.set(false);
    this.genreDropdownOpen = false;
    this.themeDropdownOpen = false;
    this.demographicDropdownOpen = false;
    this.showSavePreset = false;
    this.newPresetName = '';
    this.animes.set([]);
    this.totalCount.set(0);
    this.allLoaded.set(false);
    this.error.set(false);
    this.searchElapsedMs.set(null);
  }

  // ── Preset management ─────────────────────────────────────────────────────

  applyPreset(preset: FilterPreset): void {
    this.applyFilterState(preset.filters);
    this.showSavePreset = false;
    this.newPresetName = '';
    // Open advanced section if the preset uses any advanced filter
    const f = preset.filters;
    const hasAdvanced = !!(
      f.genre?.length ||
      f.theme?.length ||
      f.demographic?.length ||
      f.watchState?.length ||
      f.airingStatus?.length ||
      f.hasFile ||
      f.codec?.length ||
      f.mediaType?.length ||
      (f.sortBy && f.sortBy !== 'title') ||
      (f.sortOrder && f.sortOrder !== 'asc')
    );
    if (hasAdvanced) this.advancedOpen.set(true);
    this.search();
  }

  /** Applies a serialised filter snapshot to the current UI state. */
  applyFilterState(f: SavedFilterState): void {
    this.filterTitle = f.title ?? '';
    this.filterGenre.set(f.genre ?? []);
    this.genreOperator = f.genreOperator ?? 'or';
    this.filterTheme.set(f.theme ?? []);
    this.themeOperator = f.themeOperator ?? 'or';
    this.filterDemographic.set(f.demographic ?? []);
    this.demographicOperator = f.demographicOperator ?? 'or';
    this.filterWatchState = f.watchState ?? [];
    this.filterAiringStatus = f.airingStatus ?? [];
    this.filterHasFile = f.hasFile ?? 'any';
    this.filterCodec = f.codec ?? [];
    this.filterMediaType = f.mediaType ?? [];
    this.sortBy = f.sortBy ?? 'title';
    this.sortOrder = f.sortOrder ?? 'asc';
  }

  openSavePreset(): void {
    this.showSavePreset = !this.showSavePreset;
    if (this.showSavePreset) this.newPresetName = '';
  }

  confirmSavePreset(): void {
    const name = this.newPresetName.trim();
    if (!name) return;
    const id = `user-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
    const preset: FilterPreset = {
      id,
      labelKey: name,
      isBuiltIn: false,
      filters: this.snapshotFilters(),
    };
    this.presetService.savePreset(preset);
    this.savedPresets.set(this.presetService.getSavedPresets());
    this.showSavePreset = false;
    this.newPresetName = '';
  }

  cancelSavePreset(): void {
    this.showSavePreset = false;
    this.newPresetName = '';
  }

  deletePreset(id: string, event: MouseEvent): void {
    event.stopPropagation();
    this.presetService.deletePreset(id);
    this.savedPresets.set(this.presetService.getSavedPresets());
  }

  // ── Private ───────────────────────────────────────────────────────────────

  /** Returns a serialisable snapshot of the current filter panel state. */
  private snapshotFilters(): SavedFilterState {
    return {
      title: this.filterTitle || undefined,
      genre: this.filterGenre().length ? this.filterGenre() : undefined,
      genreOperator: this.filterGenre().length > 1 ? this.genreOperator : undefined,
      theme: this.filterTheme().length ? this.filterTheme() : undefined,
      themeOperator: this.filterTheme().length > 1 ? this.themeOperator : undefined,
      demographic: this.filterDemographic().length ? this.filterDemographic() : undefined,
      demographicOperator:
        this.filterDemographic().length > 1 ? this.demographicOperator : undefined,
      watchState: this.filterWatchState.length ? this.filterWatchState : undefined,
      airingStatus: this.filterAiringStatus.length ? this.filterAiringStatus : undefined,
      hasFile: this.filterHasFile !== 'any' ? this.filterHasFile : undefined,
      codec: this.filterCodec.length ? this.filterCodec : undefined,
      mediaType: this.filterMediaType.length ? this.filterMediaType : undefined,
      sortBy: this.sortBy !== 'title' ? this.sortBy : undefined,
      sortOrder: this.sortOrder !== 'asc' ? this.sortOrder : undefined,
    };
  }

  private resolveHasFile(): boolean | undefined {
    if (this.filterHasFile === 'yes') return true;
    if (this.filterHasFile === 'no') return false;
    return undefined;
  }

  private buildFilters(): AnimeFilters {
    return {
      title: this.filterTitle || undefined,
      genre: this.filterGenre().length ? this.filterGenre() : undefined,
      genreOperator: this.filterGenre().length > 1 ? this.genreOperator : undefined,
      theme: this.filterTheme().length ? this.filterTheme() : undefined,
      themeOperator: this.filterTheme().length > 1 ? this.themeOperator : undefined,
      demographic: this.filterDemographic().length ? this.filterDemographic() : undefined,
      demographicOperator:
        this.filterDemographic().length > 1 ? this.demographicOperator : undefined,
      status: this.filterWatchState.length ? this.filterWatchState : undefined,
      airingStatus: this.filterAiringStatus.length ? this.filterAiringStatus : undefined,
      hasFile: this.resolveHasFile(),
      codec: this.filterCodec.length ? this.filterCodec : undefined,
      mediaType: this.filterMediaType.length ? this.filterMediaType : undefined,
      sortBy: this.sortBy,
      sortOrder: this.sortOrder,
    };
  }

  private loadAnimes(reset: boolean): void {
    if (reset) {
      this.currentPage = 1;
      this.animes.set([]);
      this.allLoaded.set(false);
      this.loading.set(true);
      this.searchStartTime = Date.now();
      this.searchElapsedMs.set(null);
    } else {
      this.loadingMore.set(true);
    }
    this.error.set(false);

    this.animeService
      .getAnimes(this.currentPage, PAGE_SIZE, this.buildFilters())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.totalCount.set(data.totalCount);
          this.animes.update((existing) => [...existing, ...data.data]);
          this.allLoaded.set(!data.hasNextPage);
          this.loading.set(false);
          this.loadingMore.set(false);
          if (reset) this.searchElapsedMs.set(Date.now() - this.searchStartTime);
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
          this.loadingMore.set(false);
        },
      });
  }

  private loadNextPage(): void {
    if (this.loadingMore() || this.allLoaded()) return;
    this.currentPage++;
    this.loadAnimes(false);
  }

  private setupScrollSentinel(): void {
    this.scrollObserver?.disconnect();
    this.scrollObserver = null;

    if (this.allLoaded()) return;

    const elements = this.cardRefs.toArray();
    if (elements.length === 0) return;

    const sentinelIdx = Math.min(
      elements.length - 1,
      Math.max(0, elements.length - Math.floor(PAGE_SIZE / 2)),
    );
    const hostEl = elements[sentinelIdx].nativeElement;
    const targetEl = hostEl.firstElementChild;
    if (!targetEl) return;

    this.scrollObserver = new IntersectionObserver(
      (entries) => {
        if (entries[0].isIntersecting && !this.loadingMore() && !this.allLoaded()) {
          this.loadNextPage();
        }
      },
      { threshold: 0 },
    );
    this.scrollObserver.observe(targetEl);
  }
}

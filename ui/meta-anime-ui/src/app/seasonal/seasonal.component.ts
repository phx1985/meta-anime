import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal, computed, effect } from '@angular/core';
import { CommonModule, NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, forkJoin, debounceTime, switchMap, of, catchError } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SettingsService } from '../services/settings.service';
import { SonarrService } from '../services/sonarr.service';
import { FilterPresetService } from '../services/filter-preset.service';
import { UserPreferencesService } from '../services/user-preferences.service';
import { ProviderStateService } from '../services/provider-state.service';
import { PopupService } from '../services/popup.service';
import {
  SeasonalAnimeItem,
  SeasonalFilterPreset,
  SeasonalSavedFilterState,
  SeasonName,
  AnimeDetailsResponse,
  MalAnimeInfo,
  SonarrAnimeInfo,
  PlexAnimeInfo,
} from '../models/anime.models';
import {
  SonarrRootFolder,
  SonarrQualityProfile,
  SonarrLookupResult,
  SonarrAddResult,
  SonarrMonitorType,
} from '../models/sonarr.models';
import { AnimeCardComponent } from '../anime-list/anime-card/anime-card.component';
import { AnimeDetailPanelComponent } from '../anime-detail-panel/anime-detail-panel.component';
import { AnimeImageGalleryComponent } from '../anime-image-gallery/anime-image-gallery.component';
import {
  GENRE_LIST,
  THEME_LIST,
  DEMOGRAPHIC_LIST,
  SFW_GENRE_LIST,
  SFW_THEME_LIST,
  NSFW_GENRES,
  NSFW_THEMES,
} from '../constants/genre-lists';
import { MEDIA_TYPE_OPTIONS, RATING_OPTIONS } from '../anime-list/anime-list.component';
import { AddSeriesWizardComponent } from '../add-series-wizard/add-series-wizard.component';

const MIN_YEAR = 1963;

export interface SeasonPill {
  season: SeasonName;
  labelKey: string;
}

const ALL_SEASONS: SeasonPill[] = [
  { season: 'winter', labelKey: 'seasonal.season.winter' },
  { season: 'spring', labelKey: 'seasonal.season.spring' },
  { season: 'summer', labelKey: 'seasonal.season.summer' },
  { season: 'fall', labelKey: 'seasonal.season.fall' },
];

const SEASON_ORDER: SeasonName[] = ['winter', 'spring', 'summer', 'fall'];

export const SEASONAL_MAL_STATES = [
  { value: 'not_in', labelKey: 'seasonal.filter.malStatus.not_in' },
  { value: 'watching', labelKey: 'seasonal.malStatus.watching' },
  { value: 'completed', labelKey: 'seasonal.malStatus.completed' },
  { value: 'on_hold', labelKey: 'seasonal.malStatus.on_hold' },
  { value: 'plan_to_watch', labelKey: 'seasonal.malStatus.plan_to_watch' },
  { value: 'dropped', labelKey: 'seasonal.malStatus.dropped' },
];

export const SEASONAL_SORT_OPTIONS = [
  { value: 'title', labelKey: 'seasonal.filter.sort.title' },
  { value: 'malscore', labelKey: 'seasonal.filter.sort.malScore' },
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
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, TranslatePipe, AnimeCardComponent, AnimeDetailPanelComponent, NgTemplateOutlet, AnimeImageGalleryComponent, AddSeriesWizardComponent],
  templateUrl: './seasonal.component.html',
  styleUrl: './seasonal.component.scss',
})
export class SeasonalComponent {
  private animeService = inject(AnimeService);
  private settingsService = inject(SettingsService);
  private presetService = inject(FilterPresetService);
  private userPrefs = inject(UserPreferencesService);
  private destroyRef = inject(DestroyRef);
  private translateService = inject(TranslateService);
  readonly providerState = inject(ProviderStateService);
  private readonly popupService = inject(PopupService);
  private readonly sonarrService = inject(SonarrService);

  // ── Detail panel ────────────────────────────────────────────────────────────
  selectedAnimeId = signal<string | null>(null);
  selectedMalId = signal<string | null>(null);

  openAnimeDetails(id: string): void {
    this.selectedAnimeId.set(id);
    this.selectedMalId.set(null);
  }

  openDetailsByMalId(malId: string): void {
    this.selectedMalId.set(malId);
    this.selectedAnimeId.set(null);
  }

  closeAnimeDetails(): void {
    this.selectedAnimeId.set(null);
    this.selectedMalId.set(null);
  }

  /** Returns the SeasonalAnimeItem currently open in the Detail Panel (matched by malId or aniDbId). */
  readonly selectedSeasonalItem = computed<SeasonalAnimeItem | null>(() => {
    const malId = this.selectedMalId();
    const aniDbId = this.selectedAnimeId();
    if (!malId && !aniDbId) return null;
    return this.animes().find(a =>
      (malId && a.malId === malId) ||
      (aniDbId && a.aniDbId === aniDbId)
    ) ?? null;
  });

  // ── "Add to providers" modal — rendered at SeasonalComponent level ──────────
  addModalItem = signal<SeasonalAnimeItem | null>(null);
  showAddModal = signal(false);
  addBusy = signal(false);
  addError = signal('');
  addSuccess = signal(false);
  addSonarr = signal(false);
  addMal = signal(false);
  lastAddedSonarrId = signal<number | null>(null);
  /** MAL ID of the last successfully added anime — drives the card success glow. Cleared after 2.1s. */
  lastAddedMalId = signal<string | null>(null);
  expandedProvider = signal<'sonarr' | 'mal' | null>(null);
  sonarrRootFolders = signal<SonarrRootFolder[]>([]);
  sonarrQualityProfiles = signal<SonarrQualityProfile[]>([]);
  sonarrLoadingOptions = signal(false);
  sonarrSearchTerm = signal('');
  sonarrSearchResults = signal<SonarrLookupResult[]>([]);
  sonarrSearching = signal(false);
  sonarrSearchError = signal('');
  sonarrSelected = signal<SonarrLookupResult | null>(null);
  sonarrRootFolder = signal('');
  sonarrProfileId = signal(0);
  sonarrMonitor = signal<SonarrMonitorType>('all');
  sonarrSeasonFolder = signal(true);
  addMalStatus = signal('plan_to_watch');
  addMalEpisodes = signal(0);
  addMalScore = signal(0);

  readonly sonarrMonitorTypes: { value: SonarrMonitorType; labelKey: string }[] = [
    { value: 'all', labelKey: 'seasonal.monitorTypes.all' },
    { value: 'future', labelKey: 'seasonal.monitorTypes.future' },
    { value: 'missing', labelKey: 'seasonal.monitorTypes.missing' },
    { value: 'existing', labelKey: 'seasonal.monitorTypes.existing' },
    { value: 'recent', labelKey: 'seasonal.monitorTypes.recent' },
    { value: 'pilot', labelKey: 'seasonal.monitorTypes.pilot' },
    { value: 'firstSeason', labelKey: 'seasonal.monitorTypes.firstSeason' },
    { value: 'lastSeason', labelKey: 'seasonal.monitorTypes.lastSeason' },
    { value: 'monitorSpecials', labelKey: 'seasonal.monitorTypes.monitorSpecials' },
    { value: 'unmonitorSpecials', labelKey: 'seasonal.monitorTypes.unmonitorSpecials' },
    { value: 'none', labelKey: 'seasonal.monitorTypes.none' },
  ];

  readonly malStatuses = [
    { value: 'watching', labelKey: 'seasonal.malStatus.watching' },
    { value: 'completed', labelKey: 'seasonal.malStatus.completed' },
    { value: 'on_hold', labelKey: 'seasonal.malStatus.on_hold' },
    { value: 'dropped', labelKey: 'seasonal.malStatus.dropped' },
    { value: 'plan_to_watch', labelKey: 'seasonal.malStatus.plan_to_watch' },
  ];

  private sonarrSearch$ = new Subject<string>();

  /** Maps a SeasonalAnimeItem to AnimeDetailsResponse so AnimeCardComponent can render it.
   * Provider sub-objects are only populated when the anime is tracked (smart provider icons). */
  seasonalToAnimeDetails(item: SeasonalAnimeItem): AnimeDetailsResponse {
    const malAnime: MalAnimeInfo = {
      title: item.title,
      id: item.inMal ? item.malId : undefined,
      url: item.malUrl,
      viewState: item.malViewState,
      synopsis: item.synopsis,
      genres: item.genres,
      coverImageUrl: item.coverImageUrl,
      startDate: item.startDate,
      status: item.status,
      mediaType: item.mediaType,
      rating: { myScore: 0, malMean: item.malScore ?? 0 },
    };
    const sonarrAnimes: SonarrAnimeInfo[] = item.inSonarr
      ? [{ title: item.title, url: item.sonarrUrl, exists: true }]
      : [];
    const plexAnimes: PlexAnimeInfo[] = item.inPlex
      ? [{ title: item.title, url: item.plexUrl }]
      : [];
    return {
      aniDbId: item.aniDbId ?? item.malId,
      title: item.title,
      description: item.synopsis,
      malAnimes: [malAnime],
      sonarrAnimes: sonarrAnimes.length > 0 ? sonarrAnimes : undefined,
      plexAnimes: plexAnimes.length > 0 ? plexAnimes : undefined,
    };
  }

  private readonly _now = new Date();
  private readonly _maxYear = this._now.getFullYear();
  private readonly _initSeason = getCurrentSeason(this._now.getMonth() + 1);

  // Year picker
  readonly allSeasons = ALL_SEASONS;
  readonly minYear = MIN_YEAR;
  readonly maxYear = this._maxYear;

  /** Array of years from minYear to maxYear (reversed: newest first). Used for <select>. */
  readonly yearOptions = Array.from(
    { length: this._maxYear - MIN_YEAR + 1 },
    (_, i) => this._maxYear - i,
  );

  selectedYear = signal(this._maxYear);
  selectedSeason = signal<SeasonName>(this._initSeason);

  readonly canGoPrev = computed(() => this.selectedYear() > MIN_YEAR);
  readonly canGoNext = computed(() => this.selectedYear() < this._maxYear);

  /** Previous-season arrow is disabled only when already at the very first season of the first year. */
  readonly canGoPrevSeason = computed(
    () => this.selectedYear() > MIN_YEAR || this.selectedSeason() !== 'winter',
  );
  /** Next-season arrow is disabled only when already at the very last season of the last year. */
  readonly canGoNextSeason = computed(
    () => this.selectedYear() < this._maxYear || this.selectedSeason() !== 'fall',
  );

  animes = signal<SeasonalAnimeItem[]>([]);
  loading = signal(false);
  error = signal(false);
  /** Elapsed time (ms) for the last successful seasonal data fetch. */
  fetchElapsedMs = signal<number | null>(null);
  private fetchStartTime = 0;

  // ── NSFW ──────────────────────────────────────────────────────────────────
  showNsfw = signal(true);

  // ── Filter state ──────────────────────────────────────────────────────────
  filterOpen = signal(false);
  genreDropdownOpen = false;
  themeDropdownOpen = false;
  demographicDropdownOpen = false;

  /** Search text inside each filter popup */
  genreSearch = '';
  themeSearch = '';
  demographicSearch = '';

  filterGenre = signal<string[]>([]);
  genreOperator = signal<'or' | 'and'>('or');
  filterTheme = signal<string[]>([]);
  themeOperator = signal<'or' | 'and'>('or');
  filterDemographic = signal<string[]>([]);
  demographicOperator = signal<'or' | 'and'>('or');
  filterMalStatus = signal<string[]>([]);
  filterMediaType = signal<string[]>([]);
  filterAgeRating = signal<string[]>([]);
  sortBy = signal('title');
  sortOrder = signal<'asc' | 'desc'>('asc');

  /** Displayed animes — populated asynchronously via _applyFilteredResults()
   *  so items are always :enter when they appear, giving a consistent blur-rise
   *  animation on first load AND on every filter/sort/preset change. */
  displayedAnimes = signal<SeasonalAnimeItem[]>([]);
  /** Guard for rapid filter changes — only the last change animates in. */
  private _filterAnimTimeout: ReturnType<typeof setTimeout> | null = null;

  // ── Preset state ──────────────────────────────────────────────────────────
  savedPresets = signal<SeasonalFilterPreset[]>([]);
  showSavePreset = false;
  newPresetName = '';
  /** Name of the currently active preset — shown in the filter header (mobile highlight). */
  activePresetName = signal<string | null>(null);
  /** Whether the mobile presets bottom-sheet is open. */
  presetsSheetOpen = signal(false);

  readonly malStates = SEASONAL_MAL_STATES;
  readonly sortOptions = SEASONAL_SORT_OPTIONS;
  readonly mediaTypeOptions = MEDIA_TYPE_OPTIONS;
  readonly ratingOptions = RATING_OPTIONS;

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
    if (this.filterGenre().length) n++;
    if (this.filterTheme().length) n++;
    if (this.filterDemographic().length) n++;
    if (this.filterMalStatus().length) n++;
    if (this.filterMediaType().length) n++;
    if (this.filterAgeRating().length) n++;
    if (this.sortBy() !== 'title' || this.sortOrder() !== 'asc') n++;
    return n;
  }

  /** Short human-readable summary of current filters for the collapsed header */
  get filterSummary(): string {
    const parts: string[] = [];
    const genres = this.filterGenre();
    if (genres.length) {
      const sep = genres.length > 1 ? ` ${this.genreOperator().toUpperCase()} ` : '';
      parts.push(genres.map((k) => this.translateService.instant(`genres.${k}`)).join(sep));
    }
    const themes = this.filterTheme();
    if (themes.length) {
      const sep = themes.length > 1 ? ` ${this.themeOperator().toUpperCase()} ` : '';
      parts.push(themes.map((k) => this.translateService.instant(`genres.${k}`)).join(sep));
    }
    const demographics = this.filterDemographic();
    if (demographics.length) {
      const sep = demographics.length > 1 ? ` ${this.demographicOperator().toUpperCase()} ` : '';
      parts.push(demographics.map((k) => this.translateService.instant(`genres.${k}`)).join(sep));
    }
    const malStatuses = this.filterMalStatus();
    if (malStatuses.length) {
      parts.push(
        malStatuses
          .map((s) =>
            this.translateService.instant(
              s === 'not_in' ? 'seasonal.filter.malStatus.not_in' : `animeCard.malStatus.${s}`,
            ),
          )
          .join(', '),
      );
    }
    const mediaTypes = this.filterMediaType();
    if (mediaTypes.length) {
      parts.push(
        mediaTypes.map((t) => this.translateService.instant(`animeCard.mediaType.${t}`)).join(', '),
      );
    }
    const ageRatings = this.filterAgeRating();
    if (ageRatings.length) {
      parts.push(
        ageRatings
          .map((v) => this.translateService.instant(`animeList.filter.ratings.${v}`))
          .join(', '),
      );
    }
    const isDefaultSort = this.sortBy() === 'title' && this.sortOrder() === 'asc';
    if (!isDefaultSort) {
      const sortOpt = SEASONAL_SORT_OPTIONS.find((o) => o.value === this.sortBy());
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
      result = result.filter((a) => {
        const animeGenres = (a.genres ?? []).map((g) => g.toLowerCase());
        return isAnd
          ? genres.every((g) => animeGenres.includes(g.toLowerCase()))
          : genres.some((g) => animeGenres.includes(g.toLowerCase()));
      });
    }

    // Theme filter (themes are stored in the same genres array in MAL data)
    const themes = this.filterTheme();
    if (themes.length > 0) {
      const isAnd = this.themeOperator() === 'and';
      result = result.filter((a) => {
        const animeGenres = (a.genres ?? []).map((g) => g.toLowerCase());
        return isAnd
          ? themes.every((t) => animeGenres.includes(t.toLowerCase()))
          : themes.some((t) => animeGenres.includes(t.toLowerCase()));
      });
    }

    // Demographic filter (demographics are stored in the same genres array in MAL data)
    const demographics = this.filterDemographic();
    if (demographics.length > 0) {
      const isAnd = this.demographicOperator() === 'and';
      result = result.filter((a) => {
        const animeGenres = (a.genres ?? []).map((g) => g.toLowerCase());
        return isAnd
          ? demographics.every((d) => animeGenres.includes(d.toLowerCase()))
          : demographics.some((d) => animeGenres.includes(d.toLowerCase()));
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
      const statusSelected = malStatuses.filter((s) => s !== 'not_in');

      result = result.filter((a) => {
        // ── Not-in-MAL branch ──────────────────────────────────────────────
        if (!a.inMal) return notInSelected;

        // ── In-MAL branch ─────────────────────────────────────────────────
        if (statusSelected.length === 0) return false; // only 'not_in' active

        // If the anime is in MAL but malViewState is missing (cache miss /
        // data gap), keep it visible rather than hiding it silently.
        if (!a.malViewState) return true;

        return statusSelected.some((s) => a.malViewState === s);
      });
    }

    // Media type filter
    const mediaTypes = this.filterMediaType();
    if (mediaTypes.length > 0) {
      result = result.filter((a) =>
        mediaTypes.some((t) => (a.mediaType ?? '').toLowerCase() === t.toLowerCase()),
      );
    }

    // Age rating filter — exclusive: only items with a matching rating pass.
    const ageRatings = this.filterAgeRating();
    if (ageRatings.length > 0) {
      result = result.filter(
        (a) => !!a.ageRating && ageRatings.some((r) => r.toLowerCase() === a.ageRating!.toLowerCase()),
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
    this.settingsService
      .getShowNsfw()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((show) => this.showNsfw.set(show));

    this.translateService.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((e) => this.langSignal.set(e.lang));

    this.savedPresets.set(this.presetService.getSeasonalPresets());

    // Notify app-root to retract the bottom nav bar while any mobile sheet is open.
    effect(() => {
      this.popupService.childOverlayOpen.set(this.filterOpen() || this.presetsSheetOpen());
    });
    this.destroyRef.onDestroy(() => this.popupService.childOverlayOpen.set(false));

    // Restore and auto-apply the last used filters when the setting is enabled.
    if (this.userPrefs.autoApplyLastFilters()) {
      const raw = this.userPrefs.loadLastSeasonalFilters();
      if (raw) {
        try {
          const restored = JSON.parse(raw) as SeasonalSavedFilterState;
          this.applyFilterState(restored);
        } catch {
          // Corrupt storage entry — silently discard.
        }
      }
    }

    // Initial load
    this._loadCurrent();

    // Wire up Sonarr search debounce for the add-to-providers modal
    this.sonarrSearch$
      .pipe(
        debounceTime(350),
        switchMap((term) => {
          if (!term.trim()) return of([]);
          this.sonarrSearching.set(true);
          this.sonarrSearchError.set('');
          return this.sonarrService.lookup(term).pipe(
            catchError(() => {
              this.sonarrSearchError.set(this.translateService.instant('seasonal.sonarrSearchError'));
              return of([]);
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((results) => {
        this.sonarrSearchResults.set(results);
        this.sonarrSearching.set(false);
        if (this.sonarrSearchTerm().startsWith('tvdb:') && results.length === 1 && !results[0].alreadyInLibrary) {
          this.sonarrSelected.set(results[0]);
        }
      });
  }

  // ── Year navigation ──────────────────────────────────────────────────────
  prevYear(): void {
    if (!this.canGoPrev()) return;
    this.selectedYear.update((y) => y - 1);
    this._loadCurrent();
  }

  nextYear(): void {
    if (!this.canGoNext()) return;
    this.selectedYear.update((y) => y + 1);
    this._loadCurrent();
  }

  setYear(year: number): void {
    const clamped = Math.max(MIN_YEAR, Math.min(this._maxYear, year));
    if (clamped === this.selectedYear()) return;
    this.selectedYear.set(clamped);
    this._loadCurrent();
  }

  // ── Season selection ─────────────────────────────────────────────────────
  selectSeason(season: SeasonName): void {
    if (this.selectedSeason() === season) return;
    this.selectedSeason.set(season);
    this._loadCurrent();
  }

  // ── Season micro-navigation (with year wrapping) ──────────────────────────
  prevSeason(): void {
    const idx = SEASON_ORDER.indexOf(this.selectedSeason());
    if (idx === 0) {
      // winter → fall of previous year
      if (!this.canGoPrev()) return;
      this.selectedYear.update((y) => y - 1);
      this.selectedSeason.set('fall');
    } else {
      this.selectedSeason.set(SEASON_ORDER[idx - 1]);
    }
    this._loadCurrent();
  }

  nextSeason(): void {
    const idx = SEASON_ORDER.indexOf(this.selectedSeason());
    if (idx === SEASON_ORDER.length - 1) {
      // fall → winter of next year
      if (!this.canGoNext()) return;
      this.selectedYear.update((y) => y + 1);
      this.selectedSeason.set('winter');
    } else {
      this.selectedSeason.set(SEASON_ORDER[idx + 1]);
    }
    this._loadCurrent();
  }

  // ── Filter panel ─────────────────────────────────────────────────────────

  toggleFilterPanel(): void {
    this.filterOpen.update((v) => !v);
  }

  // ── Genre / Theme / Demographic popups ───────────────────────────────────

  /** Clears the active preset name when the user manually modifies any filter. */
  private invalidatePreset(): void {
    if (this.activePresetName()) this.activePresetName.set(null);
  }

  toggleGenreDropdown(event: MouseEvent): void {
    event.stopPropagation();
    this.genreDropdownOpen = !this.genreDropdownOpen;
    if (this.genreDropdownOpen) {
      this.themeDropdownOpen = false;
      this.demographicDropdownOpen = false;
    }
  }

  toggleGenre(genre: string, event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
    this.filterGenre.update((v) =>
      v.includes(genre) ? v.filter((g) => g !== genre) : [...v, genre],
    );
    if (this.filterGenre().length <= 1) this.genreOperator.set('or');
  }

  clearGenres(event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
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
    this.invalidatePreset();
    this.filterTheme.update((v) =>
      v.includes(theme) ? v.filter((t) => t !== theme) : [...v, theme],
    );
    if (this.filterTheme().length <= 1) this.themeOperator.set('or');
  }

  clearThemes(event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
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
    this.invalidatePreset();
    this.filterDemographic.update((v) =>
      v.includes(demographic) ? v.filter((d) => d !== demographic) : [...v, demographic],
    );
    if (this.filterDemographic().length <= 1) this.demographicOperator.set('or');
  }

  clearDemographics(event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
    this.filterDemographic.set([]);
  }

  // ── MAL status pills ──────────────────────────────────────────────────────

  toggleMalStatus(status: string): void {
    this.invalidatePreset();
    this.filterMalStatus.update((current) => {
      if (current.includes(status)) {
        // Deselect: remove this pill
        return current.filter((s) => s !== status);
      }
      // Select: enforce mutual exclusivity between 'not_in' and the status pills.
      //  • Selecting 'not_in'  → clear all status pills first
      //  • Selecting a status  → clear 'not_in' first
      if (status === 'not_in') {
        return ['not_in'];
      }
      return [...current.filter((s) => s !== 'not_in'), status];
    });
  }

  // ── Media type pills ──────────────────────────────────────────────────────

  toggleMediaType(type: string): void {
    this.invalidatePreset();
    this.filterMediaType.update((current) =>
      current.includes(type) ? current.filter((t) => t !== type) : [...current, type],
    );
  }

  toggleAgeRating(rating: string): void {
    this.invalidatePreset();
    this.filterAgeRating.update((current) =>
      current.includes(rating) ? current.filter((r) => r !== rating) : [...current, rating],
    );
  }

  // ── Sort ──────────────────────────────────────────────────────────────────

  setGenreOperator(op: 'or' | 'and', event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
    this.genreOperator.set(op);
  }

  setThemeOperator(op: 'or' | 'and', event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
    this.themeOperator.set(op);
  }

  setDemographicOperator(op: 'or' | 'and', event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
    this.demographicOperator.set(op);
  }

  setSortBy(value: string): void {
    this.invalidatePreset();
    this.sortBy.set(value);
  }

  /** Returns true when this MAL status pill should be visually muted (its group is excluded). */
  isMalStatusMuted(status: string): boolean {
    const active = this.filterMalStatus();
    if (active.length === 0 || active.includes(status)) return false;
    // 'not_in' is muted when any real status is active, and vice versa
    if (status === 'not_in') return active.some((s) => s !== 'not_in');
    return active.includes('not_in');
  }

  toggleSortOrder(): void {
    this.invalidatePreset();
    this.sortOrder.update((o) => (o === 'asc' ? 'desc' : 'asc'));
  }

  // ── Reset ─────────────────────────────────────────────────────────────────

  resetFilters(): void {
    this.filterGenre.set([]);
    this.genreOperator.set('or');
    this.filterTheme.set([]);
    this.themeOperator.set('or');
    this.filterDemographic.set([]);
    this.demographicOperator.set('or');
    this.filterMalStatus.set([]);
    this.filterMediaType.set([]);
    this.filterAgeRating.set([]);
    this.sortBy.set('title');
    this.sortOrder.set('asc');
    this.genreDropdownOpen = false;
    this.themeDropdownOpen = false;
    this.demographicDropdownOpen = false;
    this.activePresetName.set(null);
    this._applyFilteredResults();
  }

  /** Apply the current filter state and close the filter sheet. */
  applyFilters(): void {
    this._applyFilteredResults();
    this.filterOpen.set(false);
  }

  // ── Preset methods ────────────────────────────────────────────────────────

  applyPreset(preset: SeasonalFilterPreset): void {
    const f = preset.filters;
    this.filterGenre.set(f.genre ?? []);
    this.genreOperator.set(f.genreOperator ?? 'or');
    this.filterTheme.set(f.theme ?? []);
    this.themeOperator.set(f.themeOperator ?? 'or');
    this.filterDemographic.set(f.demographic ?? []);
    this.demographicOperator.set(f.demographicOperator ?? 'or');
    this.filterMalStatus.set(f.malStatus ?? []);
    this.filterMediaType.set(f.mediaType ?? []);
    this.filterAgeRating.set(f.ageRating ?? []);
    this.sortBy.set(f.sortBy ?? 'title');
    this.sortOrder.set(f.sortOrder ?? 'asc');
    this.genreDropdownOpen = false;
    this.themeDropdownOpen = false;
    this.demographicDropdownOpen = false;
    this.activePresetName.set(preset.label);
    this.presetsSheetOpen.set(false);
    this._applyFilteredResults();
    this.showSavePreset = false;
    this.filterOpen.set(false);
  }

  openSavePreset(): void {
    this.presetsSheetOpen.set(false);
    this.showSavePreset = !this.showSavePreset;
    if (this.showSavePreset) {
      this.filterOpen.set(true);
      const summary = this.filterSummary;
      const noFilterText = this.translateService.instant('seasonal.filter.noFilterActive');
      this.newPresetName = summary !== noFilterText ? summary : '';
    }
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
    this.newPresetName = '';
  }

  cancelSavePreset(): void {
    this.showSavePreset = false;
    this.newPresetName = '';
  }

  deletePreset(id: string, event: MouseEvent): void {
    event.stopPropagation();
    this.presetService.deleteSeasonalPreset(id);
    this.savedPresets.set(this.presetService.getSeasonalPresets());
  }

  private _snapshotFilterState(): SeasonalSavedFilterState {
    return {
      genre: this.filterGenre().length ? this.filterGenre() : undefined,
      genreOperator: this.filterGenre().length > 1 ? this.genreOperator() : undefined,
      theme: this.filterTheme().length ? this.filterTheme() : undefined,
      themeOperator: this.filterTheme().length > 1 ? this.themeOperator() : undefined,
      demographic: this.filterDemographic().length ? this.filterDemographic() : undefined,
      demographicOperator:
        this.filterDemographic().length > 1 ? this.demographicOperator() : undefined,
      malStatus: this.filterMalStatus().length ? this.filterMalStatus() : undefined,
      mediaType: this.filterMediaType().length ? this.filterMediaType() : undefined,
      ageRating: this.filterAgeRating().length ? this.filterAgeRating() : undefined,
      sortBy: this.sortBy() !== 'title' ? this.sortBy() : undefined,
      sortOrder: this.sortOrder() !== 'asc' ? this.sortOrder() : undefined,
    };
  }

  private applyFilterState(state: SeasonalSavedFilterState): void {
    this.filterGenre.set(state.genre ?? []);
    this.genreOperator.set(state.genreOperator ?? 'or');
    this.filterTheme.set(state.theme ?? []);
    this.themeOperator.set(state.themeOperator ?? 'or');
    this.filterDemographic.set(state.demographic ?? []);
    this.demographicOperator.set(state.demographicOperator ?? 'or');
    this.filterMalStatus.set(state.malStatus ?? []);
    this.filterMediaType.set(state.mediaType ?? []);
    this.filterAgeRating.set(state.ageRating ?? []);
    this.sortBy.set(state.sortBy ?? 'title');
    this.sortOrder.set(state.sortOrder ?? 'asc');
  }

  // ── Animation helper ─────────────────────────────────────────────────────
  /** Clear the grid instantly and repopulate it in the next event-loop tick.
   *  Because all items are removed from the DOM and re-added, Angular marks
   *  every card as :enter — so query(':enter') in @gridAnimation always fires
   *  the blur-rise stagger, regardless of whether the items were there before.
   *  A clearTimeout guard prevents rapid changes from causing multiple flashes. */
  private _applyFilteredResults(): void {
    // Save current filters if auto-apply is enabled
    if (this.userPrefs.autoApplyLastFilters()) {
      this.userPrefs.saveLastSeasonalFilters(JSON.stringify(this._snapshotFilterState()));
    }

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
    const year = this.selectedYear();
    const season = this.selectedSeason();

    this.error.set(false);
    this.fetchElapsedMs.set(null);

    // Always show the loading spinner immediately when the season/year changes
    // so the user gets instant visual feedback rather than stale data lingering.
    this.loading.set(true);
    this.displayedAnimes.set([]); // clear grid instantly to reveal the spinner

    this.fetchStartTime = Date.now();
    this.animeService
      .getSeasonalAnimes(year, season)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.animes.set(data);
          this.loading.set(false);
          this.fetchElapsedMs.set(Date.now() - this.fetchStartTime);
          this._applyFilteredResults();
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
          this.animes.set([]); // clear stale data on error so empty state shows
          this.displayedAnimes.set([]); // clear displayed grid immediately
        },
      });
  }

  // ── Add to providers modal ────────────────────────────────────────────────

  /** Called by the detail panel's "Add to Library" button. */
  onDetailPanelAddRequested(): void {
    const item = this.selectedSeasonalItem();
    if (!item) return;
    this.openAddModal(item);
  }

  openAddModal(seasonal: SeasonalAnimeItem): void {
    this.addModalItem.set(seasonal);
    this.addSonarr.set(!seasonal.inSonarr && this.providerState.sonarrEnabled());
    this.addMal.set(!seasonal.inMal);
    this.addError.set('');
    this.addSuccess.set(false);
    this.lastAddedSonarrId.set(null);
    this.addMalStatus.set('plan_to_watch');
    this.addMalEpisodes.set(0);
    this.addMalScore.set(0);

    if (!seasonal.inSonarr && this.providerState.sonarrEnabled()) {
      this.expandedProvider.set('sonarr');
    } else if (!seasonal.inMal) {
      this.expandedProvider.set('mal');
    } else {
      this.expandedProvider.set(null);
    }

    this.showAddModal.set(true);
    this.popupService.addProviderModalOpen.set(true);

    if (!seasonal.inSonarr && this.providerState.sonarrEnabled()) {
      this.initSonarrSection();
    }
  }

  closeAddModal(): void {
    this.showAddModal.set(false);
    this.popupService.addProviderModalOpen.set(false);
    this.expandedProvider.set(null);
  }

  toggleAccordion(provider: 'sonarr' | 'mal'): void {
    const next = this.expandedProvider() === provider ? null : provider;
    this.expandedProvider.set(next);
    if (next === 'sonarr' && this.addSonarr()) this.ensureSonarrSearch();
  }

  toggleProvider(provider: 'sonarr' | 'mal'): void {
    if (provider === 'sonarr') {
      const next = !this.addSonarr();
      this.addSonarr.set(next);
      if (next) { this.expandedProvider.set('sonarr'); this.initSonarrSection(); }
    } else {
      const next = !this.addMal();
      this.addMal.set(next);
      if (next) this.expandedProvider.set('mal');
    }
  }

  onSonarrSearchInput(term: string): void {
    this.sonarrSearchTerm.set(term);
    this.sonarrSelected.set(null);
    this.sonarrSearch$.next(term);
  }

  selectSonarrResult(item: SonarrLookupResult): void {
    if (item.alreadyInLibrary) return;
    this.sonarrSelected.set(item);
  }

  get addModalInMal(): boolean { return this.addModalItem()?.inMal ?? false; }
  get addModalInSonarr(): boolean { return this.addModalItem()?.inSonarr ?? false; }
  get addModalCoverImageUrl(): string | undefined { return this.addModalItem()?.coverImageUrl; }
  get addModalDisplayTitle(): string { return this.addModalItem()?.title ?? ''; }
  get addModalAnythingSelected(): boolean {
    return (!this.addModalInSonarr && this.addSonarr()) || (!this.addModalInMal && this.addMal());
  }

  confirmAdd(): void {
    if (this.addBusy() || !this.addModalAnythingSelected) return;
    this.addBusy.set(true);
    this.addError.set('');
    this.doAddSonarr(() => {
      this.doAddMal(() => {
        this.addSuccess.set(true);
        this.addBusy.set(false);
        const addedMalId = this.addModalItem()?.malId ?? null;
        setTimeout(() => {
          this.closeAddModal();
          // Close the detail panel so the glowing card animation is fully visible.
          this.closeAnimeDetails();
          if (addedMalId) {
            this.lastAddedMalId.set(addedMalId);
            setTimeout(() => this.lastAddedMalId.set(null), 2100);
          }
        }, 1500);
      });
    });
  }

  private getSonarrSearchTerm(): string {
    const seasonal = this.addModalItem()!;
    if (seasonal.tvdbId) return `tvdb:${seasonal.tvdbId}`;
    if (seasonal.englishTitle?.trim()) return seasonal.englishTitle.trim();
    return seasonal.title;
  }

  private initSonarrSection(): void {
    const term = this.getSonarrSearchTerm();
    this.sonarrSearchTerm.set(term);
    this.sonarrSelected.set(null);
    this.sonarrSearchResults.set([]);
    this.sonarrSearchError.set('');
    this.sonarrSearch$.next('');
    this.sonarrSearch$.next(term);
    this.loadSonarrOptions();
  }

  private ensureSonarrSearch(): void {
    if (this.sonarrSearchResults().length === 0 && !this.sonarrSearching()) {
      const term = this.getSonarrSearchTerm();
      this.sonarrSearchTerm.set(term);
      this.sonarrSearch$.next('');
      this.sonarrSearch$.next(term);
    }
    this.loadSonarrOptions();
  }

  private loadSonarrOptions(): void {
    if (this.sonarrRootFolders().length > 0 || this.sonarrLoadingOptions()) return;
    this.sonarrLoadingOptions.set(true);
    forkJoin({
      opts: this.sonarrService.getOptions(),
      settings: this.settingsService.getAll(),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ opts, settings }) => {
          this.sonarrRootFolders.set(opts.rootFolders);
          this.sonarrQualityProfiles.set(opts.qualityProfiles);
          const getDefault = (key: string) => settings.find((s) => s.key === key)?.value ?? '';
          const defaultRootFolder = getDefault('Sonarr:DefaultRootFolder');
          const defaultProfileName = getDefault('Sonarr:DefaultQualityProfileName');
          const defaultSeasonFolder = getDefault('Sonarr:DefaultSeasonFolder');
          const defaultMonitor = getDefault('Sonarr:DefaultMonitor');
          const folder = (defaultRootFolder ? opts.rootFolders.find((f) => f.path === defaultRootFolder) : null) ?? opts.rootFolders[0];
          if (folder) this.sonarrRootFolder.set(folder.path);
          const profile = (defaultProfileName ? opts.qualityProfiles.find((p) => p.name === defaultProfileName) : null) ?? opts.qualityProfiles[0];
          if (profile) this.sonarrProfileId.set(profile.id);
          if (defaultSeasonFolder) this.sonarrSeasonFolder.set(defaultSeasonFolder === 'true');
          if (defaultMonitor && this.sonarrMonitorTypes.some((t) => t.value === defaultMonitor))
            this.sonarrMonitor.set(defaultMonitor as SonarrMonitorType);
          this.sonarrLoadingOptions.set(false);
        },
        error: () => {
          this.addError.set(this.translateService.instant('seasonal.sonarrOptionsError'));
          this.sonarrLoadingOptions.set(false);
        },
      });
  }

  private doAddSonarr(onSuccess: () => void): void {
    const seasonal = this.addModalItem()!;
    if (seasonal.inSonarr || !this.addSonarr()) { onSuccess(); return; }
    const selected = this.sonarrSelected();
    if (!selected) {
      this.addError.set(this.translateService.instant('seasonal.sonarrNoneSelected'));
      this.addBusy.set(false);
      return;
    }
    this.sonarrService.addSeries({
      tvdbId: selected.tvdbId,
      rootFolderPath: this.sonarrRootFolder(),
      qualityProfileId: this.sonarrProfileId(),
      monitor: this.sonarrMonitor(),
      seasonFolder: this.sonarrSeasonFolder(),
      malId: seasonal.malId,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result: SonarrAddResult) => {
        this.lastAddedSonarrId.set(result.sonarrId);
        this._patchSeasonalItem(seasonal.malId, {
          inSonarr: true,
          ...(result.sonarrUrl ? { sonarrUrl: result.sonarrUrl } : {}),
        });
        onSuccess();
      },
      error: (err) => {
        this.addError.set(this._extractErrorMessage(err, 'seasonal.sonarrAddError'));
        this.addBusy.set(false);
      },
    });
  }

  private doAddMal(onSuccess: () => void): void {
    const seasonal = this.addModalItem()!;
    if (seasonal.inMal || !this.addMal()) { onSuccess(); return; }
    this.animeService.updateMalListStatus(seasonal.malId, {
      status: this.addMalStatus(),
      numWatchedEpisodes: this.addMalEpisodes(),
      score: this.addMalScore(),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this._patchSeasonalItem(seasonal.malId, { inMal: true });
        onSuccess();
      },
      error: (err) => {
        const sonarrId = this.lastAddedSonarrId();
        if (sonarrId !== null) {
          this.sonarrService.removeSeries(sonarrId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
            next: () => {
              this._patchSeasonalItem(seasonal.malId, { inSonarr: false, sonarrUrl: undefined });
              this.lastAddedSonarrId.set(null);
            },
            error: () => {
              this._patchSeasonalItem(seasonal.malId, { inSonarr: false });
              this.lastAddedSonarrId.set(null);
            },
          });
        }
        this.addError.set(this._extractErrorMessage(err, 'seasonal.malAddError'));
        this.addBusy.set(false);
      },
    });
  }

  /** Immutably patches a SeasonalAnimeItem in both `animes` and `displayedAnimes` signals.
   *  Replacing the object reference is required for OnPush change detection to fire on cards. */
  private _patchSeasonalItem(malId: string, patch: Partial<SeasonalAnimeItem>): void {
    const apply = (items: SeasonalAnimeItem[]) =>
      items.map((a) => (a.malId === malId ? { ...a, ...patch } : a));
    this.animes.update(apply);
    this.displayedAnimes.update(apply);
  }

  private _extractErrorMessage(err: unknown, fallbackKey: string): string {
    const e = err as Record<string, unknown> | null | undefined;
    if (!e) return this.translateService.instant(fallbackKey);
    const body = e['error'];
    if (typeof body === 'string' && body.length > 0) return body;
    if (Array.isArray(body) && body.length > 0) {
      const first = body[0];
      if (first !== null && typeof first === 'object') {
        const obj = first as Record<string, unknown>;
        const msg = obj['errorMessage'] ?? obj['message'];
        if (typeof msg === 'string') return msg;
      }
    }
    if (body && typeof body === 'object') {
      const obj = body as Record<string, unknown>;
      const msg = obj['message'] ?? obj['title'] ?? obj['errorMessage'];
      if (typeof msg === 'string') return msg;
    }
    return this.translateService.instant(fallbackKey);
  }
}

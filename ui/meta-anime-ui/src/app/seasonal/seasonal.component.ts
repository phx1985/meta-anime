import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal, computed, effect } from '@angular/core';
import { CommonModule, NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, forkJoin, debounceTime, switchMap, of, catchError, firstValueFrom } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { ActivatedRoute, Router } from '@angular/router';
import { SettingsService } from '../services/settings.service';
import { NotificationService } from '../services/notification.service';
import { SonarrService } from '../services/sonarr.service';
import { FilterPresetService } from '../services/filter-preset.service';
import { UserPreferencesService } from '../services/user-preferences.service';
import { ProviderStateService } from '../services/provider-state.service';
import { PopupService } from '../services/popup.service';
import {
  SeasonalAnimeItem,
  SeasonalSuggestionItem,
  SeasonalSuggestionScope,
  SeasonalFilterPreset,
  SeasonalSavedFilterState,
  SeasonName,
  AnimeDetailsResponse,
  MalAnimeInfo,
  SonarrAnimeInfo,
  PlexAnimeInfo,
  FilterPreset,
  SavedFilterState,
} from '../models/anime.models';
import {
  SonarrRootFolder,
  SonarrQualityProfile,
  SonarrLookupResult,
  SonarrAddResult,
  SonarrMonitorType,
} from '../models/sonarr.models';
import { AnimeCardComponent } from '../anime-list/anime-card/anime-card.component';
import { ScoreBadgeComponent } from '../anime-list/anime-card/score-badge/score-badge.component';
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
import { StorageKeys } from '../constants/storage-keys';
import { AddSeriesWizardComponent } from '../add-series-wizard/add-series-wizard.component';
import { UiSelectComponent, UiSelectOption } from '../shared/ui/ui-select/ui-select.component';
import { FpPopoverComponent } from '../shared/ui/fp-popover/fp-popover.component';
import { PillGroupComponent } from '../shared/ui/pill-group/pill-group.component';
import { PresetChipsComponent } from '../shared/ui/preset-chips/preset-chips.component';
import { UiStatePanelComponent } from '../shared/ui/ui-state-panel/ui-state-panel.component';

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

/** Discovery filters for the "For you" deck. Persisted as one versioned blob. */
export interface ForYouFilters {
  /** Media-type buckets: 'tv' | 'movie' | 'ova'. Empty = all formats. */
  formats: string[];
  status: 'all' | 'airing' | 'upcoming' | 'finished';
  /** null = any score. Unrated upcoming titles always pass. */
  minScore: number | null;
  hideSequels: boolean;
  /** Must match at least one tag when non-empty. */
  includeGenres: string[];
  /** Drops the card when it carries any of these tags. */
  excludeGenres: string[];
}

/** Two-step batch add: confirm the targets, then watch each item land. */
export interface BatchAddState {
  phase: 'confirm' | 'progress' | 'complete';
  items: SeasonalSuggestionItem[];
  targets: { mal: boolean; sonarr: boolean; searchMissing: boolean };
  /** Items already processed (drives "Adding 2 of 4"). */
  index: number;
  results: Array<{ malId: string; title: string; ok: boolean; error?: string }>;
  busy: boolean;
}

const DEFAULT_FOR_YOU_FILTERS: ForYouFilters = {
  formats: [],
  status: 'all',
  minScore: null,
  hideSequels: false,
  includeGenres: [],
  excludeGenres: [],
};

@Component({
  selector: 'app-seasonal',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, TranslatePipe, ScoreBadgeComponent, AnimeCardComponent, AnimeDetailPanelComponent, NgTemplateOutlet, AnimeImageGalleryComponent, AddSeriesWizardComponent, UiSelectComponent, FpPopoverComponent, PillGroupComponent, PresetChipsComponent, UiStatePanelComponent],
  templateUrl: './seasonal.component.html',
  styleUrl: './seasonal.component.scss',
})
export class SeasonalComponent {
  private animeService = inject(AnimeService);
  private settingsService = inject(SettingsService);
  private notifService = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
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
    // Deck cards from another season are not in `animes()` (the grid only holds the current
    // season), so the lookup must also cover the suggestion pool — otherwise the side panel
    // opens with no item and renders empty.
    const pool: SeasonalAnimeItem[] = [
      ...this.animes(),
      ...this.activeSlots(),
      ...this.reserveQueue(),
    ];

    return pool.find(a =>
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
  /** Default for "search for missing episodes" on add (overridable per action). */
  sonarrSearchMissing = signal(false);
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

  // ── Shared-component adapters (app-ui-preset-chips / app-ui-fp-popover) ─────
  // The shared chip row takes the generic FilterPreset display shape; apply is
  // resolved back to the original SeasonalFilterPreset by id so the frozen
  // preset flows (and their SeasonalSavedFilterState payloads) are untouched.

  /** Saved presets projected into the shared chip row's display shape. */
  readonly presetChipsItems = computed<FilterPreset[]>(() =>
    this.savedPresets().map((p) => ({
      id: p.id,
      labelKey: p.label,
      isBuiltIn: false,
      filters: p.filters as SavedFilterState,
    })),
  );

  /** Applies the saved preset behind a shared chip-row click. */
  applyPresetById(id: string): void {
    const preset = this.savedPresets().find((p) => p.id === id);
    if (preset) this.applyPreset(preset);
  }

  /** Operator toggle coming from the shared fp-popover (it already stopped propagation). */
  onFilterOperatorChange(type: 'genre' | 'theme' | 'demographic', op: 'or' | 'and'): void {
    this.invalidatePreset();
    this.setFilterOperator(type, op);
  }

  /** Re-runs the current season fetch (error-state Retry CTA). */
  retryLoad(): void {
    this._loadCurrent();
  }

  // ── ui-select option sets (labels translated here — the component takes final strings) ──

  sortSelectOptions(): UiSelectOption[] {
    return this.sortOptions.map((o) => ({ label: this.translateService.instant(o.labelKey), value: o.value }));
  }
  readonly mediaTypeOptions = MEDIA_TYPE_OPTIONS;
  readonly ratingOptions = RATING_OPTIONS;

  /** Genre list filtered by NSFW setting */
  readonly filteredGenreList = computed(() => (this.showNsfw() ? GENRE_LIST : SFW_GENRE_LIST));

  /** Theme list filtered by NSFW setting */
  readonly filteredThemeList = computed(() => (this.showNsfw() ? THEME_LIST : SFW_THEME_LIST));

  /** Label shown on the genre dropdown trigger button */
  readonly genreLabel = computed(() => {
    this.langSignal();
    return this.makeFilterLabel('genres', this.filterGenre());
  });

  /** Label shown on the theme dropdown trigger button */
  readonly themeLabel = computed(() => {
    this.langSignal();
    return this.makeFilterLabel('genres', this.filterTheme());
  });

  /** Label shown on the demographic dropdown trigger button */
  readonly demographicLabel = computed(() => {
    this.langSignal();
    return this.makeFilterLabel('genres', this.filterDemographic());
  });

  readonly demographicOptions = DEMOGRAPHIC_LIST;

  /** Reactive language tag — changes when the user switches language. */
  private readonly langSignal = signal(
    this.translateService.getCurrentLang() || this.translateService.getFallbackLang() || 'en',
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
    // Deep link from an "Anime added" notification: open the panel, then clear the param so
    // a refresh does not reopen it.
    this.route.queryParamMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => {
        const malId = params.get('malId');
        if (!malId) return;
        this.openDetailsByMalId(malId);
        this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
      });

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

  // ── Generic filter helpers ────────────────────────────────────────────────

  /** Builds the truncated label string for a filter dropdown button. */
  private makeFilterLabel(translationPrefix: string, filters: string[]): string | null {
    if (filters.length === 0) return null;
    const t = (k: string) => this.translateService.instant(`${translationPrefix}.${k}`);
    if (filters.length <= 2) return filters.map(t).join(', ');
    return `${t(filters[0])}, ${t(filters[1])} +${filters.length - 2}`;
  }

  /** Toggles a value in the specified filter signal and resets its operator when ≤1 selected. */
  private toggleFilterValue(type: 'genre' | 'theme' | 'demographic', value: string): void {
    const map = {
      genre: { filter: this.filterGenre, operator: this.genreOperator },
      theme: { filter: this.filterTheme, operator: this.themeOperator },
      demographic: { filter: this.filterDemographic, operator: this.demographicOperator },
    } as const;
    const { filter, operator } = map[type];
    filter.update((v) => (v.includes(value) ? v.filter((x) => x !== value) : [...v, value]));
    if (filter().length <= 1) operator.set('or');
  }

  /** Clears all selections for the specified filter type. */
  private clearFilterType(type: 'genre' | 'theme' | 'demographic'): void {
    const map = {
      genre: this.filterGenre,
      theme: this.filterTheme,
      demographic: this.filterDemographic,
    } as const;
    map[type].set([]);
  }

  /** Sets the AND/OR operator for the specified filter type. */
  private setFilterOperator(type: 'genre' | 'theme' | 'demographic', op: 'or' | 'and'): void {
    const map = {
      genre: this.genreOperator,
      theme: this.themeOperator,
      demographic: this.demographicOperator,
    } as const;
    map[type].set(op);
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
    this.toggleFilterValue('genre', genre);
  }

  clearGenres(event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
    this.clearFilterType('genre');
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
    this.toggleFilterValue('theme', theme);
  }

  clearThemes(event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
    this.clearFilterType('theme');
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
    this.toggleFilterValue('demographic', demographic);
  }

  clearDemographics(event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
    this.clearFilterType('demographic');
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
    this.setFilterOperator('genre', op);
  }

  setThemeOperator(op: 'or' | 'and', event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
    this.setFilterOperator('theme', op);
  }

  setDemographicOperator(op: 'or' | 'and', event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
    this.setFilterOperator('demographic', op);
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

  // ── "For you" deck ────────────────────────────────────────────────────────
  /** Full ranked pool returned by the API (32 deep, so filters cannot starve the deck). */
  readonly rawPicks = signal<SeasonalSuggestionItem[]>([]);
  /** Picks the user passed on — removed from the visible pool but kept for stats. */
  readonly passedIds = signal<ReadonlySet<string>>(new Set());
  /** Multi-selection for the batch bar. */
  /**
   * Selection is keyed by MAL id and holds the *item itself*, not an index and not a boolean.
   * Scrolling, filtering, paging or a deck reshuffle can therefore never lose or scramble a
   * chosen title — the payload needed to add it travels with the selection.
   */
  readonly selectedPicks = signal<ReadonlyMap<string, SeasonalSuggestionItem>>(new Map());
  /** Discovery filters, persisted under one versioned localStorage key. */
  readonly forYouFilters = signal<ForYouFilters>(this._loadForYouFilters());

  /** Pool after the discovery filters (ranking order is preserved). */
  readonly filteredPicks = computed(() => this.rawPicks().filter((item) => this._matchesFilters(item)));
  /** Visible pool: filtered minus passed. Always sorted by match, so slot 1 is the best pick. */
  readonly visiblePicks = computed(() => this.filteredPicks().filter((p) => !this.passedIds().has(p.malId)));
  /** Paging through the ranked pool, 4 cards at a time. */
  readonly pageIndex = signal(0);
  readonly slideDir = signal<'next' | 'prev' | null>(null);
  readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.visiblePicks().length / SeasonalComponent.DECK_SLOTS)));

  /** Slots currently on screen — the current page of the ranked pool. */
  readonly activeSlots = computed(() => {
    const size = SeasonalComponent.DECK_SLOTS;
    const page = Math.min(this.pageIndex(), this.pageCount() - 1);
    return this.visiblePicks().slice(page * size, page * size + size);
  });

  /** "Showing 1–4 of 16" arithmetic. */
  readonly pageWindow = computed(() => {
    const total = this.visiblePicks().length;
    const size = SeasonalComponent.DECK_SLOTS;
    const page = Math.min(this.pageIndex(), this.pageCount() - 1);
    const start = total === 0 ? 0 : page * size + 1;
    const end = Math.min((page + 1) * size, total);
    return { start, end, total, page, pages: this.pageCount() };
  });
  /** Remaining ranked candidates behind the visible slots. */
  readonly reserveQueue = computed(() => this.visiblePicks().slice(SeasonalComponent.DECK_SLOTS));
  /** Everything the user ticked, including titles the current filters no longer show. */
  readonly selectedItems = computed(() => [...this.selectedPicks().values()]);
  readonly batchState = signal<BatchAddState | null>(null);
  readonly batchMessage = signal<string | null>(null);
  /** Cards mid fade-out — used by the template for the leaving animation. */
  readonly leavingMalIds = signal<ReadonlySet<string>>(new Set());
  /** Cards that just landed in a slot — used for the pop-in animation. */
  readonly enteringMalIds = signal<ReadonlySet<string>>(new Set());
  readonly suggestionsLoading = signal(false);
  /** Scope of the suggestion pool (persisted in localStorage). */
  readonly suggestionScope = this.userPrefs.seasonalSuggestionScope;
  readonly scopeOptions: { value: SeasonalSuggestionScope; labelKey: string }[] = [
    { value: 'current', labelKey: 'seasonal.forYou.scope.current' },
    { value: 'last2', labelKey: 'seasonal.forYou.scope.last2' },
    { value: 'last4', labelKey: 'seasonal.forYou.scope.last4' },
    { value: 'custom', labelKey: 'seasonal.forYou.scope.custom' },
  ];
  /** Inline custom-range popover. */
  readonly customRangeOpen = signal(false);
  readonly customFromYear = signal(this._now.getFullYear());
  readonly customFromSeason = signal<SeasonName>(this._initSeason);
  readonly customToYear = signal(this._now.getFullYear());
  readonly customToSeason = signal<SeasonName>(this._initSeason);
  readonly allSeasonNames = ALL_SEASONS;

  /** Hide Chinese animation (donghua) from the deck — persisted. */
  readonly excludeDonghua = this.userPrefs.seasonalExcludeDonghua;
  /** Tags that must never appear in a suggestion — persisted. */
  readonly excludedGenres = this.userPrefs.seasonalExcludedGenres;
  /** Genre-exclusion popover state + the tag list offered there. */
  /** Genre filter modal (replaces the anchored chip dropdown). */
  readonly genreModalOpen = signal(false);
  readonly genreQuery = signal('');
  readonly genreTab = signal<'include' | 'exclude'>('include');
  readonly draftInclude = signal<ReadonlySet<string>>(new Set());
  readonly draftExclude = signal<ReadonlySet<string>>(new Set());
  /** Card whose synopsis overlay is pinned open (touch devices have no hover). */
  readonly peekId = signal<string | null>(null);

  togglePeek(malId: string, event?: Event): void {
    event?.stopPropagation();
    event?.preventDefault();
    this.peekId.set(this.peekId() === malId ? null : malId);
  }
  readonly genreChoices: string[] = [
    ...GENRE_LIST, ...THEME_LIST, ...DEMOGRAPHIC_LIST,
  ].sort((a, b) => a.localeCompare(b));

  /** The deck hides itself (no empty box) when there is nothing honest to show. */
  readonly showDeck = computed(() => this.rawPicks().length >= 3);

  private static readonly DECK_SLOTS = 4;
  private static readonly DECK_FETCH = 32;
  private static readonly LEAVE_MS = 220;
  private static readonly ENTER_MS = 260;

  /** Fetches the ranked list for the current season and fills the 4 visible slots. */
  loadSuggestions(): void {
    const year = this.selectedYear();
    const season = this.selectedSeason();

    this.suggestionsLoading.set(true);
    this.rawPicks.set([]);
    this.passedIds.set(new Set());

    const scope = this.suggestionScope() as SeasonalSuggestionScope;
    const range =
      scope === 'custom'
        ? {
            fromYear: this.customFromYear(),
            fromSeason: this.customFromSeason(),
            toYear: this.customToYear(),
            toSeason: this.customToSeason(),
          }
        : undefined;

    this.animeService
      .getSeasonalSuggestions(
        year, season, SeasonalComponent.DECK_FETCH, scope, range,
        this.excludeDonghua(), [],
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (items) => {
          this.rawPicks.set(items);
          this.suggestionsLoading.set(false);
          this._flashEntering(this.activeSlots().map((s) => s.malId));
        },
        error: () => {
          // A missing deck must never break the page.
          this.rawPicks.set([]);
          this.suggestionsLoading.set(false);
        },
      });
  }

  /** Scope pill click: persist, close the popover and reload the deck. */
  setScope(scope: SeasonalSuggestionScope): void {
    this.userPrefs.setSeasonalSuggestionScope(scope);
    if (scope === 'custom') {
      this.customFromYear.set(this.selectedYear());
      this.customToYear.set(this.selectedYear());
      this.customFromSeason.set(this.selectedSeason());
      this.customToSeason.set(this.selectedSeason());
      this.customRangeOpen.set(true);
      return;
    }
    this.customRangeOpen.set(false);
    this.loadSuggestions();
  }

  /** Toggle donghua exclusion and reload the deck. */
  toggleExcludeDonghua(): void {
    this.userPrefs.setSeasonalExcludeDonghua(!this.excludeDonghua());
    this.loadSuggestions();
  }

  /** Adds/removes a tag from the exclusion list and reloads the deck. */
  toggleExcludedGenre(genre: string): void {
    const current = this.excludedGenres();
    const next = current.includes(genre) ? current.filter((g) => g !== genre) : [...current, genre];
    this.userPrefs.setSeasonalExcludedGenres(next);
    this.loadSuggestions();
  }

  isGenreExcluded(genre: string): boolean {
    return this.excludedGenres().includes(genre);
  }

  /** Applies the custom range chosen in the popover. */
  applyCustomRange(): void {
    this.customRangeOpen.set(false);
    this.userPrefs.setSeasonalSuggestionScope('custom');
    this.loadSuggestions();
  }

  /** ✕ Pass — fade the card out, then swap in the next reserve pick in the same slot. */
  passSuggestion(item: SeasonalSuggestionItem, event?: Event): void {
    event?.stopPropagation();
    this._consumeSlot(item.malId);
  }

  /**
   * Removes a card from the deck and immediately fills its slot from the reserve queue.
   * Also called after a successful "+ MAL" / "+ Sonarr" so the deck never repeats what
   * the user just added.
   */
  private _consumeSlot(malId: string): void {
    this._removeFromDeck([malId]);
  }

  /**
   * Fades the given cards out, then retires them from the pool. The slots are computed from
   * the pool, so the next ranked picks slide in automatically — always score-ordered.
   */
  private _removeFromDeck(malIds: string[]): void {
    if (malIds.length === 0) return;

    const leaving = new Set(this.leavingMalIds());
    malIds.forEach((id) => leaving.add(id));
    this.leavingMalIds.set(leaving);

    setTimeout(() => {
      const passed = new Set(this.passedIds());
      malIds.forEach((id) => passed.add(id));
      this.passedIds.set(passed);

      if (malIds.includes(this.peekId() ?? '')) this.peekId.set(null);

      const cleared = new Set(this.leavingMalIds());
      malIds.forEach((id) => cleared.delete(id));
      this.leavingMalIds.set(cleared);

      const selected = new Map(this.selectedPicks());
      malIds.forEach((id) => selected.delete(id));
      this.selectedPicks.set(selected);

      this._flashEntering(this.activeSlots().map((s) => s.malId));
    }, SeasonalComponent.LEAVE_MS);
  }

  /** ↻ New Picks — rotate the pool so the 4 slots show the next ranked batch. */
  shuffleDeck(): void {
    const visible = this.visiblePicks();
    if (visible.length <= SeasonalComponent.DECK_SLOTS) return;

    const head = visible.slice(0, SeasonalComponent.DECK_SLOTS).map((p) => p.malId);
    const leaving = new Set(head);
    this.leavingMalIds.set(leaving);

    setTimeout(() => {
      // Rotate the underlying pool: the shown heads go last, the next ranked picks come first.
      const pool = this.rawPicks();
      const isHead = (id: string) => head.includes(id);
      this.rawPicks.set([...pool.filter((p) => !isHead(p.malId)), ...pool.filter((p) => isHead(p.malId))]);

      this.leavingMalIds.set(new Set());
      this._flashEntering(this.activeSlots().map((s) => s.malId));
    }, SeasonalComponent.LEAVE_MS);
  }

  private _flashEntering(malIds: string[]): void {
    const entering = new Set(this.enteringMalIds());
    malIds.forEach((id) => entering.add(id));
    this.enteringMalIds.set(entering);

    setTimeout(() => {
      const next = new Set(this.enteringMalIds());
      malIds.forEach((id) => next.delete(id));
      this.enteringMalIds.set(next);
    }, SeasonalComponent.ENTER_MS);
  }

  /** Badge tier: emerald at 80%+, accent violet 65–79%, teal below. */
  matchTier(item: SeasonalSuggestionItem): 'high' | 'mid' | 'low' {
    const pct = this.matchPercent(item);
    return pct >= 80 ? 'high' : pct >= 65 ? 'mid' : 'low';
  }

  /** Genre/theme chips that are NOT already quoted in the "because you liked" callout. */
  /** Tags the app treats as adult; hidden from cards and the modal unless NSFW is on. */
  private static readonly NSFW_TAGS = new Set(['hentai', 'ecchi', 'erotica']);

  isNsfwTag(tag: string): boolean {
    return SeasonalComponent.NSFW_TAGS.has(tag.toLowerCase());
  }

  /** Genres + themes ship together from MAL; the "because" tags are no longer subtracted. */
  private _visibleTags(item: SeasonalSuggestionItem): string[] {
    const showNsfw = this.showNsfw();
    return (item.genres ?? []).filter((g) => showNsfw || !this.isNsfwTag(g));
  }

  genreChips(item: SeasonalSuggestionItem): string[] {
    return this._visibleTags(item).slice(0, 3).map((g) => {
      const key = `genres.${g}`;
      const translated = this.translateService.instant(key);
      return translated === key ? g : translated;
    });
  }

  hiddenGenreCount(item: SeasonalSuggestionItem): number {
    return Math.max(0, this._visibleTags(item).length - 3);
  }

  /** Semantic format badge: colour-coded so the format reads at a glance. */
  formatClass(item: SeasonalSuggestionItem): string {
    const type = (item.mediaType ?? '').toLowerCase();
    const known = ['tv', 'movie', 'ova', 'ona', 'special', 'music'];
    return `badge-format format-${known.includes(type) ? type : 'unknown'}`;
  }

  formatLabel(item: SeasonalSuggestionItem): string {
    const type = (item.mediaType ?? '').toUpperCase();
    if (!type) return '?';
    const key = `seasonal.formats.${type.toLowerCase()}`;
    const translated = this.translateService.instant(key);
    return translated === key ? type : translated;
  }

  /** Adult chip test that also works on an already-translated label. */
  isNsfwChip(label: string): boolean {
    if (this.isNsfwTag(label)) return true;
    return this.genreChoices.some(
      (g) => this.isNsfwTag(g) && this.translateService.instant(`genres.${g}`) === label,
    );
  }

  /** True when a visible tag is adult, so the chip can render in the warning palette. */
  hasNsfwTag(item: SeasonalSuggestionItem): boolean {
    return this.showNsfw() && (item.genres ?? []).some((g) => this.isNsfwTag(g));
  }

  /** "Oct 2026 – Dec 2026" (or a single formatted date when only one is known). */
  airDateLabel(item: SeasonalSuggestionItem): string {
    const start = this.formatMonth(item.startDate);
    const end = this.formatMonth(item.endDate);
    // A same-month movie would otherwise read "Oct 2026 – Oct 2026".
    if (start && end && end !== start) return `${start} – ${end}`;
    return start ?? '';
  }

  private formatMonth(value?: string | null): string | null {
    if (!value) return null;
    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return null;
    return d.toLocaleDateString((this.translateService.currentLang() || 'en'), { month: 'short', year: 'numeric' });
  }

  /** Airing status pill text — the one line that tells you if you can watch it now. */
  airingLabel(item: SeasonalSuggestionItem): string {
    const status = (item.status ?? '').toLowerCase();
    if (status === 'currently_airing') {
      return this.translateService.instant('seasonal.forYou.airingProgress', {
        current: item.airedEpisodesEst ?? '?',
        total: item.numEpisodes ?? '?',
      });
    }
    if (status === 'finished_airing') {
      return this.translateService.instant('seasonal.forYou.finishedEps', { total: item.numEpisodes ?? '?' });
    }
    if (status === 'not_yet_aired') {
      const date = item.startDate
        ? new Date(item.startDate).toLocaleDateString((this.translateService.currentLang() || 'en'), {
            month: 'short',
            day: 'numeric',
            year: 'numeric',
          })
        : '?';
      return this.translateService.instant('seasonal.forYou.upcomingDate', { date });
    }
    return '';
  }

  /** Season tag shown on the poster when the pool spans several seasons. */
  seasonTag(item: SeasonalSuggestionItem): string {
    if (this.suggestionScope() === 'current' || !item.startDate) return '';
    const d = new Date(item.startDate);
    if (Number.isNaN(d.getTime())) return '';
    const month = d.getMonth() + 1;
    const season = month <= 3 ? 'winter' : month <= 6 ? 'spring' : month <= 9 ? 'summer' : 'fall';
    return `${this.translateService.instant(`seasonal.season.${season}`)} ${d.getFullYear()}`.toUpperCase();
  }

  /** Match score → integer percentage for the badge. */
  matchPercent(item: SeasonalSuggestionItem): number {
    return Math.round(Math.max(0, Math.min(1, item.matchScore)) * 100);
  }

  /** Titles behind the callout (already formatted, ready to bold). */
  reasonTitles(item: SeasonalSuggestionItem): string {
    return (item.reasons?.[0]?.because ?? []).map((b) => b.title).join(' & ');
  }

  /** Tags behind the callout. */
  reasonTags(item: SeasonalSuggestionItem): string {
    return (item.reasons?.[0]?.tags ?? []).slice(0, 2).join(', ');
  }

  /** "Because you liked A & B · Genre, Theme" — never fabricated. */
  reasonText(item: SeasonalSuggestionItem): string {
    const reason = item.reasons?.[0];
    if (!reason || reason.kind !== 'taste' || reason.because.length === 0) {
      return this.translateService.instant('seasonal.forYou.popularReason');
    }

    const names = reason.because.map((b) => b.title).join(' & ');
    const tags = reason.tags.slice(0, 2).join(', ');
    return tags
      ? this.translateService.instant('seasonal.forYou.becauseLiked', { titles: names, tags })
      : this.translateService.instant('seasonal.forYou.becauseLikedNoTags', { titles: names });
  }

  /** Opens the side detail panel for a deck card (aniDbId when known, else the MAL id). */
  openDeckDetail(item: SeasonalSuggestionItem, event?: Event): void {
    event?.stopPropagation();
    // The panel resolves MAL ids via /animes/by-mal/{malId}; the AniDB id is not the local
    // primary key, so passing it to /animes/{id} is exactly what produced the 404.
    this.openDetailsByMalId(item.malId);
  }

  /** Opens the existing add-to-provider modal for a deck card. */
  onDeckAdd(item: SeasonalSuggestionItem, event?: Event): void {
    event?.stopPropagation();
    this.openAddModal(item);
  }

  /** "+ Sonarr" / "+ MAL" quick-adds: same modal, collapsed onto the chosen provider. */
  onDeckAddProvider(item: SeasonalSuggestionItem, provider: 'sonarr' | 'mal'): void {
    this.openAddModal(item);
    this.addSonarr.set(provider === 'sonarr');
    this.addMal.set(provider === 'mal');
    this.expandedProvider.set(provider);
  }

  /** Paging back when a filter or a pass empties the current page. */
  private readonly _clampPage = effect(() => {
    const pages = this.pageCount();
    if (this.pageIndex() > pages - 1) this.pageIndex.set(pages - 1);
  });

  /** Page indices for the dot track. */
  readonly dotsRange = computed(() => Array.from({ length: this.pageCount() }, (_, i) => i));

  /** Label of the applied custom range, shown inside the Custom pill. */
  readonly customRangeLabel = computed(() => {
    if (this.suggestionScope() !== 'custom') return '';
    const name = (v: string) => this.translateService.instant(`seasonal.season.${v}`);
    const from = `${name(this.customFromSeason())} ${this.customFromYear()}`;
    const to = `${name(this.customToSeason())} ${this.customToYear()}`;
    return from === to ? from : `${from} – ${to}`;
  });

  /** Failures among the rows already processed (pending rows are not failures). */
  failedBatchResult(batch: BatchAddState): number {
    return batch.results.slice(0, batch.index).filter((r) => !r.ok).length;
  }

  nextPicksPage(): void {
    if (this.pageIndex() >= this.pageCount() - 1) return;
    this.slideDir.set('next');
    this.pageIndex.update((p) => p + 1);
    setTimeout(() => this.slideDir.set(null), 200);
  }

  prevPicksPage(): void {
    if (this.pageIndex() <= 0) return;
    this.slideDir.set('prev');
    this.pageIndex.update((p) => p - 1);
    setTimeout(() => this.slideDir.set(null), 200);
  }

  goToPicksPage(page: number): void {
    const target = Math.max(0, Math.min(page, this.pageCount() - 1));
    if (target === this.pageIndex()) return;
    this.slideDir.set(target > this.pageIndex() ? 'next' : 'prev');
    this.pageIndex.set(target);
    setTimeout(() => this.slideDir.set(null), 200);
  }

  // ── "For you" discovery filters ───────────────────────────────────────────
  /** Media-type bucket used by the Format pills. */
  private _formatBucket(item: SeasonalSuggestionItem): 'tv' | 'movie' | 'ova' {
    const type = (item.mediaType ?? '').toLowerCase();
    if (type === 'movie') return 'movie';
    if (type === 'ova' || type === 'ona' || type === 'special' || type === 'music') return 'ova';
    return 'tv';
  }

  /** Airing bucket used by the Status pills. */
  private _statusBucket(item: SeasonalSuggestionItem): 'airing' | 'upcoming' | 'finished' | 'other' {
    switch ((item.status ?? '').toLowerCase()) {
      case 'currently_airing': return 'airing';
      case 'not_yet_aired': return 'upcoming';
      case 'finished_airing': return 'finished';
      default: return 'other';
    }
  }

  /**
   * "Is this a later season/part?" — the backend already drops sequels whose prequel was never
   * watched; this is the user-level "hide even those" switch.
   */
  isSequel(item: SeasonalSuggestionItem): boolean {
    if (/^sequel to\b/i.test((item.synopsis ?? '').trim())) return true;
    const title = (item.title ?? '').trim();
    if (/\b(season\s*[2-9]|[2-9](nd|rd|th)\s+season|part\s*[2-9]|cour\s*[2-9]|final season)\b/i.test(title)) return true;
    // "Cyberpunk: Edgerunners 2", "Vinland Saga II" — a trailing numeral almost always means a
    // later entry. Zero-padded years like "86" have no separator, so they stay clear.
    return /[\s:–-]([2-9]|[ivx]{2,4}|II|III|IV)$/i.test(title);
  }

  private _matchesFilters(item: SeasonalSuggestionItem, f: ForYouFilters = this.forYouFilters()): boolean {

    if (f.formats.length > 0 && !f.formats.includes(this._formatBucket(item))) return false;

    if (f.status !== 'all' && this._statusBucket(item) !== f.status) return false;

    if (f.minScore !== null) {
      // Unrated upcoming shows are legitimately unscored — they stay visible on purpose.
      const unrated = !item.malScore || item.malScore <= 0;
      if (unrated ? this._statusBucket(item) !== 'upcoming' : item.malScore < f.minScore) return false;
    }

    if (f.hideSequels && this.isSequel(item)) return false;

    const genres = (item.genres ?? []).map((g) => g.toLowerCase());
    if (f.includeGenres.length > 0 && !f.includeGenres.some((g) => genres.includes(g.toLowerCase()))) return false;
    if (f.excludeGenres.length > 0 && f.excludeGenres.some((g) => genres.includes(g.toLowerCase()))) return false;

    return true;
  }

  /** True when any Row-2 filter deviates from its default (drives the Reset link). */
  hasActiveForYouFilters(): boolean {
    const f = this.forYouFilters();
    return f.formats.length > 0 || f.status !== 'all' || f.minScore !== null ||
      f.hideSequels || f.includeGenres.length > 0 || f.excludeGenres.length > 0;
  }

  setFormats(formats: string[]): void {
    this._patchFilters({ formats });
  }

  /** Multi-select Format pill: toggles one media bucket. */
  toggleFormat(bucket: string): void {
    const current = this.forYouFilters().formats;
    this._patchFilters({ formats: current.includes(bucket) ? current.filter((f) => f !== bucket) : [...current, bucket] });
  }

  setStatusFilter(status: ForYouFilters['status']): void {
    this._patchFilters({ status });
  }

  setMinScore(minScore: number | null): void {
    this._patchFilters({ minScore });
  }

  toggleHideSequels(): void {
    this._patchFilters({ hideSequels: !this.forYouFilters().hideSequels });
  }

  /** Genres matching the modal's search box. */
  readonly filteredGenreChoices = computed(() => {
    const q = this.genreQuery().trim().toLowerCase();
    const showNsfw = this.showNsfw();
    const base = this.genreChoices.filter((g) => showNsfw || !this.isNsfwTag(g));
    return q ? base.filter((g) => g.toLowerCase().includes(q)) : base;
  });

  /** Draft filters, so the Apply button can preview the result count live. */
  private _draftFilters(): ForYouFilters {
    return {
      ...this.forYouFilters(),
      includeGenres: [...this.draftInclude()],
      excludeGenres: [...this.draftExclude()],
    };
  }

  readonly draftMatchCount = computed(() =>
    this.rawPicks().filter((item) => this._matchesFilters(item, this._draftFilters())).length);

  openGenreModal(): void {
    this.draftInclude.set(new Set(this.forYouFilters().includeGenres));
    this.draftExclude.set(new Set(this.forYouFilters().excludeGenres));
    this.genreQuery.set('');
    this.genreTab.set('include');
    this.genreModalOpen.set(true);
  }

  /** Click-outside dismissal for the custom-range popover (no layout shift). */
  closeCustomRangeOnOutsideClick(event: Event): void {
    if (!this.customRangeOpen()) return;
    const target = event.target as HTMLElement | null;
    if (target?.closest('.custom-range-popover') || target?.closest('.for-you__scopes')) return;
    this.customRangeOpen.set(false);
  }

  closeGenreModal(): void {
    this.genreModalOpen.set(false);
  }

  /** Neutral → Include → Exclude → Neutral, on a single screen. */
  draftGenreState(genre: string): 'neutral' | 'include' | 'exclude' {
    if (this.draftInclude().has(genre)) return 'include';
    if (this.draftExclude().has(genre)) return 'exclude';
    return 'neutral';
  }

  cycleDraftGenre(genre: string): void {
    const state = this.draftGenreState(genre);
    const include = new Set(this.draftInclude());
    const exclude = new Set(this.draftExclude());

    if (state === 'neutral') {
      include.add(genre);
    } else if (state === 'include') {
      include.delete(genre);
      exclude.add(genre);
    } else {
      exclude.delete(genre);
    }

    this.draftInclude.set(include);
    this.draftExclude.set(exclude);
  }

  /** Direct removal from the active-filters summary strip. */
  removeDraftGenre(genre: string): void {
    const include = new Set(this.draftInclude());
    const exclude = new Set(this.draftExclude());
    include.delete(genre);
    exclude.delete(genre);
    this.draftInclude.set(include);
    this.draftExclude.set(exclude);
  }

  readonly draftGenreList = computed(() => ({
    included: [...this.draftInclude()].sort((a, b) => a.localeCompare(b)),
    excluded: [...this.draftExclude()].sort((a, b) => a.localeCompare(b)),
  }));

  clearDraftTab(): void {
    if (this.genreTab() === 'include') this.draftInclude.set(new Set());
    else this.draftExclude.set(new Set());
  }

  clearDraftGenres(): void {
    this.draftInclude.set(new Set());
    this.draftExclude.set(new Set());
  }

  applyGenreModal(): void {
    this._patchFilters({
      includeGenres: [...this.draftInclude()],
      excludeGenres: [...this.draftExclude()],
    });
    this.genreModalOpen.set(false);
  }

  resetForYouFilters(): void {
    this.forYouFilters.set({ ...DEFAULT_FOR_YOU_FILTERS });
    this._saveForYouFilters(this.forYouFilters());
  }

  /** Clears the selection whenever the pool changes shape (season/scope switch). */
  clearSelection(): void {
    this.selectedPicks.set(new Map());
  }

  private _patchFilters(patch: Partial<ForYouFilters>): void {
    const next: ForYouFilters = { ...this.forYouFilters(), ...patch };
    this.forYouFilters.set(next);
    this._saveForYouFilters(next);

  }

  private _loadForYouFilters(): ForYouFilters {
    try {
      const raw = localStorage.getItem(StorageKeys.seasonalForYouFilters);
      if (!raw) return { ...DEFAULT_FOR_YOU_FILTERS };
      const parsed = JSON.parse(raw) as Partial<ForYouFilters>;
      return { ...DEFAULT_FOR_YOU_FILTERS, ...parsed };
    } catch {
      return { ...DEFAULT_FOR_YOU_FILTERS };
    }
  }

  private _saveForYouFilters(filters: ForYouFilters): void {
    try {
      localStorage.setItem(StorageKeys.seasonalForYouFilters, JSON.stringify(filters));
    } catch { /* quota / private mode — filters stay in memory */ }
  }

  // ── Multi-selection + batch actions ───────────────────────────────────────
  isSelected(malId: string): boolean {
    return this.selectedPicks().has(malId);
  }

  togglePickSelection(malId: string, event?: Event): void {
    event?.stopPropagation();
    event?.preventDefault();

    const next = new Map(this.selectedPicks());
    if (next.has(malId)) {
      next.delete(malId);
    } else {
      const item = this.visiblePicks().find((p) => p.malId === malId) ?? this.activeSlots().find((p) => p.malId === malId);
      if (!item) return;
      next.set(malId, item);
    }
    this.selectedPicks.set(next);
  }

  selectAllVisible(): void {
    const next = new Map(this.selectedPicks());
    this.activeSlots().forEach((p) => next.set(p.malId, p));
    this.selectedPicks.set(next);
  }

  /** × Pass (N) — retires every selected card in one animated batch. */
  passSelected(): void {
    const ids = [...this.selectedPicks().keys()];
    if (ids.length === 0) return;
    this.selectedPicks.set(new Map());
    this._removeFromDeck(ids);
  }

  /** + Add (N) — opens the confirmation step; nothing is dispatched yet. */
  openBatchAddModal(): void {
    const items = this.selectedItems();
    if (items.length === 0) return;

    if (this.providerState.sonarrEnabled()) this.refreshSearchMissingDefault();

    this.batchState.set({
      phase: 'confirm',
      items,
      targets: { mal: true, sonarr: this.providerState.sonarrEnabled(), searchMissing: this.sonarrSearchMissing() },
      index: 0,
      results: [],
      busy: false,
    });
  }

  /** Removes one title from the pending batch (and unchecks it on the deck behind). */
  removeBatchItem(malId: string): void {
    const state = this.batchState();
    if (!state || state.busy) return;

    const items = state.items.filter((i) => i.malId !== malId);
    if (items.length === 0) {
      this.dismissBatchModal();
    } else {
      this.batchState.set({ ...state, items });
    }

    const selected = new Map(this.selectedPicks());
    selected.delete(malId);
    this.selectedPicks.set(selected);
  }

  clearBatchItems(): void {
    this.dismissBatchModal();
    this.selectedPicks.set(new Map());
  }

  setBatchTarget(target: 'mal' | 'sonarr' | 'searchMissing', value: boolean): void {
    this.batchState.update((st) => (st ? { ...st, targets: { ...st.targets, [target]: value } } : st));
  }

  dismissBatchModal(): void {
    this.batchState.set(null);
  }

  /**
   * Sequential on purpose: MAL and Sonarr both rate-limit, and the user asked to *see* each
   * item land. 150 ms between calls keeps the progress list readable.
   */
  async startBatchAdd(): Promise<void> {
    const state = this.batchState();
    if (!state || state.busy) return;

    const items = state.items;
    const targets = state.targets;

    this.batchState.set({
      ...state,
      phase: 'progress',
      busy: true,
      index: 0,
      results: items.map((i) => ({ malId: i.malId, title: i.title, ok: false })),
    });

    let sonarrDefaults: { rootFolderPath: string; qualityProfileId: number } | null = null;
    if (targets.sonarr && this.providerState.sonarrEnabled()) {
      try {
        const opts = await firstValueFrom(this.sonarrService.getOptions());
        const folder = opts.rootFolders[0];
        const profile = opts.qualityProfiles[0];
        if (folder && profile) sonarrDefaults = { rootFolderPath: folder.path, qualityProfileId: profile.id };
      } catch { /* Sonarr unreachable → MAL only, reported per item */ }
    }

    const results: BatchAddState['results'] = [];

    for (let i = 0; i < items.length; i++) {
      const item = items[i];
      let ok = true;
      let error: string | undefined;

      try {
        if (targets.mal && !item.inMal) {
          await firstValueFrom(this.animeService.updateMalListStatus(item.malId, {
            status: 'plan_to_watch',
            numWatchedEpisodes: 0,
            score: 0,
          }));
        }

        if (targets.sonarr && sonarrDefaults && !item.inSonarr && item.tvdbId) {
          await firstValueFrom(this.sonarrService.addSeries({
            tvdbId: Number(item.tvdbId),
            rootFolderPath: sonarrDefaults.rootFolderPath,
            qualityProfileId: sonarrDefaults.qualityProfileId,
            monitor: 'all',
            seasonFolder: true,
            malId: item.malId,
            searchForMissingEpisodes: targets.searchMissing,
          }));
        }
      } catch (e) {
        ok = false;
        error = e instanceof Error ? e.message : 'request failed';
      }

      results.push({ malId: item.malId, title: item.title, ok, error });
      this.batchState.update((st) => (st ? { ...st, index: i + 1, results: [...results] } : st));

      if (i < items.length - 1) await new Promise((r) => setTimeout(r, 150));
    }

    const added = results.filter((r) => r.ok).map((r) => r.malId);
    const failed = results.filter((r) => !r.ok).map((r) => r.malId);

    this.batchState.update((st) => (st ? { ...st, phase: 'complete', busy: false, results } : st));
    this.selectedPicks.set(new Map(state.items.filter((i) => failed.includes(i.malId)).map((i) => [i.malId, i])));
    this._removeFromDeck(added);

    // Batch summary — one row, not one per item, so the bell stays readable.
    if (added.length > 0 || failed.length > 0) {
      this.notifService.pushOrReplace({
        id: `batch-${Date.now()}`,
        severity: failed.length === 0 ? 'success' : 'warning',
        titleKey: this.translateService.instant('notifications.animeAdded.multiTitle'),
        messageKey: this.translateService.instant('notifications.animeAdded.multiMessage', {
          count: added.length,
        }),
        action: {
          labelKey: this.translateService.instant('notifications.animeAdded.viewActivity', { count: added.length }),
          route: ['/activity'],
          queryParams: { filter: 'adds', batchId: `batch-${Date.now()}` },
        },
      });
    }

    if (failed.length === 0) setTimeout(() => this.dismissBatchModal(), 2000);
  }

  // ── Loading ──────────────────────────────────────────────────────────────  // ── Loading ──────────────────────────────────────────────────────────────
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
    this.loadSuggestions();
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
      // Re-read the flag every time the modal opens: the options loader bails out once
      // cached, which is why a setting changed in Settings never reached this switch.
      this.refreshSearchMissingDefault();
      this.initSonarrSection();
    }
  }

  /** Pulls Sonarr:SearchForMissingEpisodes fresh from the settings store. */
  private refreshSearchMissingDefault(): void {
    this.settingsService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) =>
          this.sonarrSearchMissing.set(
            list.find((s) => s.key === 'Sonarr:SearchForMissingEpisodes')?.value === 'true',
          ),
        error: () => {},
      });
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
        if (addedMalId) {
          const added = this.addModalItem();
          this.notifService.pushOrReplace({
            id: `anime-added-${addedMalId}`,
            severity: 'success',
            titleKey: this.translateService.instant('notifications.animeAdded.singleTitle'),
            messageKey: this.translateService.instant('notifications.animeAdded.singleMessage', {
              title: added?.title ?? '',
            }),
            action: {
              labelKey: this.translateService.instant('notifications.animeAdded.viewDetails'),
              route: ['/seasonal'],
              queryParams: { malId: addedMalId },
            },
          });
        }
        setTimeout(() => {
          this.closeAddModal();
          // Close the detail panel so the glowing card animation is fully visible.
          this.closeAnimeDetails();
          if (addedMalId) {
            this.lastAddedMalId.set(addedMalId);
            setTimeout(() => this.lastAddedMalId.set(null), 2100);
            // If that card came from the "For you" deck, retire it and refill the slot.
            this._consumeSlot(addedMalId);
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
          const defaultSearchMissing = getDefault('Sonarr:SearchForMissingEpisodes');
          if (defaultSearchMissing) this.sonarrSearchMissing.set(defaultSearchMissing === 'true');
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
      searchForMissingEpisodes: this.sonarrSearchMissing(),
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

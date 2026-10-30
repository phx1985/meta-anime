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
import { SEASONAL_MAL_STATES, SEASONAL_SORT_OPTIONS } from '../seasonal/seasonal.component';
import { AddSeriesWizardComponent } from '../add-series-wizard/add-series-wizard.component';
import { UiSelectComponent, UiSelectOption } from '../shared/ui/ui-select/ui-select.component';

@Component({
  selector: 'app-mal-search',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, TranslatePipe, AnimeCardComponent, AnimeDetailPanelComponent, NgTemplateOutlet, AnimeImageGalleryComponent, AddSeriesWizardComponent, UiSelectComponent],
  templateUrl: './mal-search.component.html',
  styleUrl: './mal-search.component.scss',
})
export class MalSearchComponent {
  private animeService = inject(AnimeService);
  private settingsService = inject(SettingsService);
  private presetService = inject(FilterPresetService);
  private userPrefs = inject(UserPreferencesService);
  private destroyRef = inject(DestroyRef);
  private translateService = inject(TranslateService);
  readonly providerState = inject(ProviderStateService);
  private readonly popupService = inject(PopupService);
  private readonly sonarrService = inject(SonarrService);

  // ── Search state ─────────────────────────────────────────────────────────
  searchQuery = signal('');
  lastSearchedQuery = signal('');
  private search$ = new Subject<string>();

  submitSearch(): void {
    const q = this.searchQuery().trim();
    if (q.length < 3) return;
    this.search$.next(q);
  }

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

  // ── "Add to providers" modal ──────────────────────────────────────────────
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

  /** Maps a SeasonalAnimeItem to AnimeDetailsResponse so AnimeCardComponent can render it. */
  searchItemToAnimeDetails(item: SeasonalAnimeItem): AnimeDetailsResponse {
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

  animes = signal<SeasonalAnimeItem[]>([]);
  loading = signal(false);
  error = signal(false);

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

  /** Displayed animes — populated asynchronously via _applyFilteredResults() */
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
              s === 'not_in' ? 'seasonal.filter.malStatus.not_in' : `seasonal.malStatus.${s}`,
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
      ? this.translateService.instant('malSearch.filter.noFilterActive')
      : parts.join(' · ');
  }

  /** Filtered and sorted anime list – applied client-side from the already-loaded data. */
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

    // Theme filter
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

    // Demographic filter
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
    const malStatuses = this.filterMalStatus();
    if (malStatuses.length > 0) {
      const notInSelected = malStatuses.includes('not_in');
      const statusSelected = malStatuses.filter((s) => s !== 'not_in');

      result = result.filter((a) => {
        if (!a.inMal) return notInSelected;
        if (statusSelected.length === 0) return false;
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

    // Age rating filter
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

    // Wire up the debounced search
    this.search$
      .pipe(
        debounceTime(0),
        switchMap((query) => {
          if (!query) {
            // Empty sentinel from resetFilters(): drop the search state and any in-flight request.
            this.loading.set(false);
            this.error.set(false);
            this.animes.set([]);
            this.displayedAnimes.set([]);
            this.lastSearchedQuery.set('');
            return of([]);
          }
          this.loading.set(true);
          this.error.set(false);
          this.animes.set([]);
          this.displayedAnimes.set([]);
          this.lastSearchedQuery.set(query);
          return this.animeService.searchMalAnimeWithDetails(query).pipe(
            catchError(() => {
              this.error.set(true);
              this.loading.set(false);
              return of([]);
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((data) => {
        this.animes.set(data);
        this.loading.set(false);
        this._applyFilteredResults();
      });

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
        return current.filter((s) => s !== status);
      }
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
    if (status === 'not_in') return active.some((s) => s !== 'not_in');
    return active.includes('not_in');
  }

  toggleSortOrder(): void {
    this.invalidatePreset();
    this.sortOrder.update((o) => (o === 'asc' ? 'desc' : 'asc'));
  }

  // ── Reset ─────────────────────────────────────────────────────────────────

  resetFilters(): void {
    this.searchQuery.set('');
    this.lastSearchedQuery.set('');
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
    this.animes.set([]);
    this.error.set(false);
    this._applyFilteredResults();
    // Empty sentinel: cancels any in-flight search request (switchMap) and clears its loading state.
    this.search$.next('');
  }

  /** Apply the current filter state and close the filter sheet. */
  applyFilters(): void {
    const q = this.searchQuery().trim();
    if (q.length >= 3) {
      this.submitSearch();
    } else {
      this._applyFilteredResults();
    }
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
      const noFilterText = this.translateService.instant('malSearch.filter.noFilterActive');
      this.newPresetName = summary !== noFilterText ? summary : '';
    }
  }

  confirmSavePreset(): void {
    const name = this.newPresetName.trim();
    if (!name) return;
    const preset: SeasonalFilterPreset = {
      id: `malSearch-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
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

  // ── Animation helper ─────────────────────────────────────────────────────
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

  // ── Add to providers modal ────────────────────────────────────────────────

  /** Called by the detail panel's "Add to Library" button. */
  onDetailPanelAddRequested(): void {
    const item = this.selectedSeasonalItem();
    if (!item) return;
    this.openAddModal(item);
  }

  openAddModal(item: SeasonalAnimeItem): void {
    this.addModalItem.set(item);
    this.addSonarr.set(!item.inSonarr && this.providerState.sonarrEnabled());
    this.addMal.set(!item.inMal);
    this.addError.set('');
    this.addSuccess.set(false);
    this.lastAddedSonarrId.set(null);
    this.addMalStatus.set('plan_to_watch');
    this.addMalEpisodes.set(0);
    this.addMalScore.set(0);

    if (!item.inSonarr && this.providerState.sonarrEnabled()) {
      this.expandedProvider.set('sonarr');
    } else if (!item.inMal) {
      this.expandedProvider.set('mal');
    } else {
      this.expandedProvider.set(null);
    }

    this.showAddModal.set(true);
    this.popupService.addProviderModalOpen.set(true);

    if (!item.inSonarr && this.providerState.sonarrEnabled()) {
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
    const item = this.addModalItem()!;
    if (item.tvdbId) return `tvdb:${item.tvdbId}`;
    if (item.englishTitle?.trim()) return item.englishTitle.trim();
    return item.title;
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
    const item = this.addModalItem()!;
    if (item.inSonarr || !this.addSonarr()) { onSuccess(); return; }
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
      malId: item.malId,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result: SonarrAddResult) => {
        this.lastAddedSonarrId.set(result.sonarrId);
        this._patchItem(item.malId, {
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
    const item = this.addModalItem()!;
    if (item.inMal || !this.addMal()) { onSuccess(); return; }
    this.animeService.updateMalListStatus(item.malId, {
      status: this.addMalStatus(),
      numWatchedEpisodes: this.addMalEpisodes(),
      score: this.addMalScore(),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this._patchItem(item.malId, { inMal: true });
        onSuccess();
      },
      error: (err) => {
        const sonarrId = this.lastAddedSonarrId();
        if (sonarrId !== null) {
          this.sonarrService.removeSeries(sonarrId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
            next: () => {
              this._patchItem(item.malId, { inSonarr: false, sonarrUrl: undefined });
              this.lastAddedSonarrId.set(null);
            },
            error: () => {
              this._patchItem(item.malId, { inSonarr: false });
              this.lastAddedSonarrId.set(null);
            },
          });
        }
        this.addError.set(this._extractErrorMessage(err, 'seasonal.malAddError'));
        this.addBusy.set(false);
      },
    });
  }

  /** Immutably patches a SeasonalAnimeItem in both `animes` and `displayedAnimes` signals. */
  private _patchItem(malId: string, patch: Partial<SeasonalAnimeItem>): void {
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

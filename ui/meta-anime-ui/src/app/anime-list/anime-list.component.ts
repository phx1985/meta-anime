import {
  AfterViewInit,
  ChangeDetectionStrategy,
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
import { debounceTime } from 'rxjs/operators';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { NgTemplateOutlet, DatePipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SettingsService } from '../services/settings.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { FilterPresetService } from '../services/filter-preset.service';
import { UserPreferencesService } from '../services/user-preferences.service';
import { ProviderStateService } from '../services/provider-state.service';
import { PopupService } from '../services/popup.service';
import {
  AnimeDetailsResponse,
  AnimeFilters,
  FilterPreset,
  SavedFilterState,
} from '../models/anime.models';
import { AnimeCardComponent } from './anime-card/anime-card.component';
import { AnimeDetailPanelComponent } from '../anime-detail-panel/anime-detail-panel.component';
import { AnimeImageGalleryComponent } from '../anime-image-gallery/anime-image-gallery.component';
import { StorageKeys } from '../constants/storage-keys';
import { formatTableSize as formatSize, getCodecPills as codecPills } from '../utils/format.utils';

const DEFAULT_PAGE_SIZE = 20;
const DISPLAY_MODE_KEY = StorageKeys.displayMode;

export type DisplayMode = 1 | 2 | 3 | 4;

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
  { value: 'watching', labelKey: 'animeDetailPanel.malStatus.watching' },
  { value: 'completed', labelKey: 'animeDetailPanel.malStatus.completed' },
  { value: 'on_hold', labelKey: 'animeDetailPanel.malStatus.on_hold' },
  { value: 'dropped', labelKey: 'animeDetailPanel.malStatus.dropped' },
  { value: 'plan_to_watch', labelKey: 'animeDetailPanel.malStatus.plan_to_watch' },
];

export const AIRING_STATES = [
  { value: 'finished_airing', labelKey: 'animeDetailPanel.seriesStatus.finished_airing' },
  { value: 'currently_airing', labelKey: 'animeDetailPanel.seriesStatus.currently_airing' },
  { value: 'not_yet_aired', labelKey: 'animeDetailPanel.seriesStatus.not_yet_aired' },
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
  { value: 'episodes', labelKey: 'animeList.sort.episodes' },
  { value: 'avgepsize', labelKey: 'animeList.sort.avgEpSize' },
];

export const CODEC_OPTIONS = [
  { value: 'hevc', labelKey: 'animeList.filter.codecs.hevc' },
  { value: 'h264', labelKey: 'animeList.filter.codecs.h264' },
  { value: 'av1', labelKey: 'animeList.filter.codecs.av1' },
  { value: 'mpeg2', labelKey: 'animeList.filter.codecs.mpeg2' },
];

export const MEDIA_TYPE_OPTIONS = [
  { value: 'tv', labelKey: 'animeDetailPanel.mediaType.tv' },
  { value: 'movie', labelKey: 'animeDetailPanel.mediaType.movie' },
  { value: 'ova', labelKey: 'animeDetailPanel.mediaType.ova' },
  { value: 'ona', labelKey: 'animeDetailPanel.mediaType.ona' },
  { value: 'special', labelKey: 'animeDetailPanel.mediaType.special' },
  { value: 'music', labelKey: 'animeDetailPanel.mediaType.music' },
];

export const RATING_OPTIONS = [
  { value: 'g',     labelKey: 'animeList.filter.ratings.g' },
  { value: 'pg',    labelKey: 'animeList.filter.ratings.pg' },
  { value: 'pg_13', labelKey: 'animeList.filter.ratings.pg_13' },
  { value: 'r',     labelKey: 'animeList.filter.ratings.r' },
  { value: 'r+',    labelKey: 'animeList.filter.ratings.r+' },
  { value: 'rx',    labelKey: 'animeList.filter.ratings.rx' },
];

@Component({
  selector: 'app-anime-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, TranslatePipe, AnimeCardComponent, AnimeDetailPanelComponent, AnimeImageGalleryComponent, NgTemplateOutlet, DatePipe],
  templateUrl: './anime-list.component.html',
  styleUrl: './anime-list.component.scss',
})
export class AnimeListComponent implements AfterViewInit, OnDestroy {
  private animeService = inject(AnimeService);
  private settingsService = inject(SettingsService);
  private syncSignalR = inject(SyncSignalRService);
  private destroyRef = inject(DestroyRef);
  private translateService = inject(TranslateService);
  readonly presetService = inject(FilterPresetService);
  readonly userPrefs = inject(UserPreferencesService);
  readonly providerState = inject(ProviderStateService);
  private readonly popupService = inject(PopupService);

  @ViewChildren('animeCard', { read: ElementRef }) cardRefs!: QueryList<ElementRef>;

  animes = signal<AnimeDetailsResponse[]>([]);
  loading = signal(false);
  loadingMore = signal(false);
  error = signal(false);
  totalCount = signal(0);
  allLoaded = signal(false);
  displayMode = signal<DisplayMode>(2);
  layoutState = signal(0);

  // ── Table-view state ──────────────────────────────────────────────────────

  /** Results fetched per "page" (infinite-scroll batch). Configurable by the user in table view. */
  readonly pageSizeOptions = [20, 50, 100] as const;
  pageSize = signal<number>(DEFAULT_PAGE_SIZE);

  /** Current page number for table-view pagination (1-based). */
  tablePage = signal(1);
  /** Total number of pages, derived from server totalCount. */
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));
  /** Page number buttons to render in the table pagination bar (null = ellipsis). */
  readonly tablePageRange = computed<(number | null)[]>(() => {
    const total = this.totalPages();
    const current = this.tablePage();
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const pages: (number | null)[] = [1];
    if (current > 3) pages.push(null);
    const start = Math.max(2, current - 1);
    const end = Math.min(total - 1, current + 1);
    for (let i = start; i <= end; i++) pages.push(i);
    if (current < total - 2) pages.push(null);
    pages.push(total);
    return pages;
  });

  /** Active sort column for the table header indicators. Null when the active sort has no table column (e.g. malscore). */
  tableSortCol = signal<'title' | 'episodes' | 'size' | 'avgsize' | 'added' | null>('title');
  tableSortDir = signal<'asc' | 'desc'>('asc');

  searched = signal(false);
  selectedAnimeId = signal<string | null>(null);
  /** Controls the first-time onboarding animation on the first card. */
  cardHintActive = signal(!localStorage.getItem('card-hint-v1'));
  showNsfw = signal(true);
  /** Elapsed ms for the last search request (null until first search completes) */
  searchElapsedMs = signal<number | null>(null);
  /** Fallback Sonarr URL (RemoteUrl ?? Host) used for the per-anime Sonarr button when sonarr.url is null. */
  sonarrBaseUrl = signal<string | null>(null);

  /** Whether the filter body is expanded */
  filterOpen = signal(false);

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
  readonly savedPresets = this.presetService.savedPresets;
  /** Whether the "save as preset" inline form is visible */
  showSavePreset = false;
  newPresetName = '';
  /** Name of the currently active preset — shown in the filter header (mobile highlight). */
  activePresetName = signal<string | null>(null);
  /** Whether the mobile presets bottom-sheet is open. */
  presetsSheetOpen = signal(false);

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
  filterAgeRating = signal<string[]>([]);
  sortBy = 'title';
  sortOrder: 'asc' | 'desc' = 'asc';

  readonly watchingStates = WATCHING_STATES;
  readonly airingStates = AIRING_STATES;
  readonly sortOptions = SORT_OPTIONS;
  readonly codecOptions = CODEC_OPTIONS;
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

  /** Label shown on the age rating dropdown trigger button */
  readonly ageRatingLabel = computed(() => {
    this.langSignal();
    const sel = this.filterAgeRating();
    if (sel.length === 0) return null;
    const t = (k: string) => this.translateService.instant(`animeList.filter.ratings.${k}`);
    if (sel.length <= 2) return sel.map(t).join(', ');
    return `${t(sel[0])}, ${t(sel[1])} +${sel.length - 2}`;
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
    if (this.filterTitle) n++;
    if (this.filterGenre().length) n++;
    if (this.filterTheme().length) n++;
    if (this.filterDemographic().length) n++;
    if (this.filterWatchState.length) n++;
    if (this.filterAiringStatus.length) n++;
    if (this.filterHasFile !== 'any') n++;
    if (this.filterCodec.length) n++;
    if (this.filterMediaType.length) n++;
    if (this.filterAgeRating().length) n++;
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
    if (this.filterAgeRating().length) n++;
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
          .map((v) => this.translateService.instant(`animeDetailPanel.malStatus.${v}`))
          .join(', '),
      );
    }
    if (this.filterAiringStatus.length) {
      parts.push(
        this.filterAiringStatus
          .map((v) => this.translateService.instant(`animeDetailPanel.seriesStatus.${v}`))
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
          .map((v) => this.translateService.instant(`animeDetailPanel.mediaType.${v}`))
          .join(', '),
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
    const isDefaultSort= this.sortBy === 'title' && this.sortOrder === 'asc';
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
    if (saved && n >= 1 && n <= 4) {
      // Mode 4 (table) is desktop-only — fall back to grid on mobile
      const resolved = (n === 4 && window.innerWidth <= 768) ? 2 : n;
      this.displayMode.set(resolved as DisplayMode);
    }

    // Notify app-root to retract the bottom nav bar while any mobile sheet is open.
    effect(() => {
      this.popupService.childOverlayOpen.set(this.filterOpen() || this.presetsSheetOpen());
    });
    this.destroyRef.onDestroy(() => this.popupService.childOverlayOpen.set(false));

    this.settingsService
      .getShowNsfw()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((show) => this.showNsfw.set(show));

    // Load the Sonarr base URL (RemoteUrl ?? Host) for the per-anime Sonarr button fallback
    this.settingsService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (settings) => {
          const url =
            settings.find((s) => s.key === 'Sonarr:RemoteUrl')?.value?.trim() ||
            settings.find((s) => s.key === 'Sonarr:Host')?.value?.trim() ||
            null;
          this.sonarrBaseUrl.set(url);
        },
        error: () => {},
      });

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
          this.activePresetName.set(restored.activePresetName ?? null);
          this.search();
        } catch {
          // Corrupt storage entry — silently discard.
        }
      }
    }
  }

  ngAfterViewInit(): void {
    this.cardRefs.changes
      // F12: debounce rapid QueryList changes (e.g. virtual-scroll batch renders) to avoid
      // tearing down and rebuilding the IntersectionObserver on every individual card update
      .pipe(debounceTime(100), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.setupScrollSentinel());
  }

  ngOnDestroy(): void {
    this.scrollObserver?.disconnect();
  }

  // ── Collapse outside click handler ────────────────────────────────────────

  // Popup close-on-backdrop handled inline in template; nothing to close via document:click.

  // ── Table view helpers ────────────────────────────────────────────────────

  /** Click a table column header to sort server-side; second click reverses direction. */
  sortTable(col: 'title' | 'episodes' | 'size' | 'avgsize' | 'added'): void {
    const dir = this.tableSortCol() === col && this.tableSortDir() === 'asc' ? 'desc' : 'asc';
    this.tableSortCol.set(col);
    this.tableSortDir.set(dir);
    this.sortOrder = dir;

    // Keys must match SORT_OPTIONS values so the dropdown stays in sync.
    const serverFieldMap: Record<'title' | 'episodes' | 'size' | 'avgsize' | 'added', string> = {
      title: 'title',
      episodes: 'episodes',
      size: 'sizeondisk',
      avgsize: 'avgepsize',
      added: 'addeddate',
    };
    this.sortBy = serverFieldMap[col];
    this.invalidatePreset();
    if (this.searched()) this.loadAnimes(true);
  }

  getAddedDate(anime: AnimeDetailsResponse): string | null {
    const date = anime.sonarrAnimes?.[0]?.addedDate ?? null;
    if (!date) return null;
    return new Date(date).getFullYear() > 1 ? date : null;
  }

  loadMore(): void {
    if (this.loadingMore() || this.allLoaded()) return;
    this.currentPage++;
    this.loadAnimes(false);
  }

  /** Change the results-per-page batch size and reload. */
  changePageSize(value: number | string): void {
    const n = Number(value);
    if (n > 0) {
      this.pageSize.set(n);
      if (this.searched()) this.loadAnimes(true);
    }
  }

  /** Navigate to a specific page in the table view. */
  goToTablePage(page: number): void {
    const total = this.totalPages();
    if (page < 1 || page > total || page === this.tablePage()) return;
    this.loadAnimes(true, page);
  }

  /** Format raw bytes to a human-readable size string using Base-2 (1024) math. */
  formatTableSize(bytes?: number): string {
    return formatSize(bytes, (key) => this.translateService.instant(key));
  }

  /** Average file size per episode for the anime. */
  formatAvgEpSize(anime: AnimeDetailsResponse): string {
    const bytes = anime.sonarrAnimes?.[0]?.sizeOnDisk;
    const eps = this.getTableEpCount(anime);
    if (!bytes || bytes <= 0 || eps <= 0) return '—';
    return this.formatTableSize(bytes / eps);
  }

  /** Total available (on-disk) episodes across all non-specials seasons. */
  getTableEpCount(anime: AnimeDetailsResponse): number {
    return (
      anime.sonarrAnimes?.[0]?.seasonInfo
        ?.filter((s) => s.seasonNumber > 0)
        ?.reduce((sum, s) => sum + s.availableEpisodes, 0) ?? 0
    );
  }

  /** Returns flat list of genre/theme/demographic tags with their type for colour-coding. */
  getAnimeTags(
    anime: AnimeDetailsResponse,
  ): { key: string; type: 'genre' | 'theme' | 'demographic' }[] {
    return (anime.malAnimes?.[0]?.genres ?? []).map((g) => ({
      key: g,
      type: (GENRE_LIST.includes(g)
        ? 'genre'
        : THEME_LIST.includes(g)
          ? 'theme'
          : 'demographic') as 'genre' | 'theme' | 'demographic',
    }));
  }

  /** Convert a codec distribution map into a sorted array of { label, pct } pills.
   *  Filters out empty/null keys and entries with 0 count. */
  getCodecPills(dist?: { [key: string]: number }): { label: string; pct: number }[] {
    return codecPills(dist);
  }

  /** Average bytes per episode (raw, used for client-side sort). */
  private calcAvgEpBytes(anime: AnimeDetailsResponse): number {
    const bytes = anime.sonarrAnimes?.[0]?.sizeOnDisk ?? 0;
    const eps = this.getTableEpCount(anime);
    return eps > 0 ? bytes / eps : 0;
  }

  // ── Display mode ──────────────────────────────────────────────────────────

  setDisplayMode(mode: DisplayMode): void {
    if (this.displayMode() === mode) return;
    const wasTable = this.displayMode() === 4;
    this.displayMode.set(mode);
    this.layoutState.update((v) => v + 1);
    localStorage.setItem(DISPLAY_MODE_KEY, String(mode));
    if (wasTable && mode !== 4) {
      this.filterCodec = [];
      this.filterHasFile = 'any';
      this.invalidatePreset();
      if (this.searched()) this.loadAnimes(true);
    }
  }

  // ── Filter panel collapse ─────────────────────────────────────────────────

  toggleFilterPanel(): void {
    this.filterOpen.update((v) => !v);
  }

  /** Clears the active preset name when the user manually modifies any filter. */
  protected invalidatePreset(): void {
    if (this.activePresetName()) this.activePresetName.set(null);
  }

  // ── Advanced section collapse ─────────────────────────────────────────────

  toggleAdvanced(): void {
    this.advancedOpen.update((v) => !v);
  }

  // ── Sort order toggle ─────────────────────────────────────────────────────

  toggleSortOrder(): void {
    this.invalidatePreset();
    this.sortOrder = this.sortOrder === 'asc' ? 'desc' : 'asc';
  }

  onSortByChange(): void {
    this.invalidatePreset();
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
    this.invalidatePreset();
    this.filterGenre.update((v) => this.togglePill(v, genre));
    // Auto-reset operator to OR when ≤ 1 genre selected (operator toggle is hidden)
    if (this.filterGenre().length <= 1) this.genreOperator = 'or';
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
    this.filterTheme.update((v) => this.togglePill(v, theme));
    if (this.filterTheme().length <= 1) this.themeOperator = 'or';
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
    this.filterDemographic.update((v) => this.togglePill(v, demographic));
    if (this.filterDemographic().length <= 1) this.demographicOperator = 'or';
  }

  clearDemographics(event: MouseEvent): void {
    event.stopPropagation();
    this.invalidatePreset();
    this.filterDemographic.set([]);
  }

  // ── Search / Reset ────────────────────────────────────────────────────────

  search(): void {
    // Sync table sort indicators with filter sort state.
    // Maps SORT_OPTIONS values → table column ids. null = no column highlighted.
    const reverseFieldMap: Record<string, 'title' | 'episodes' | 'size' | 'avgsize' | 'added' | null> = {
      title: 'title',
      episodes: 'episodes',
      sizeondisk: 'size',
      totalsizedisk: 'size',
      avgepsize: 'avgsize',
      addeddate: 'added',
      malscore: null,
      myscore: null,
      startdate: null,
      enddate: null,
    };
    this.tableSortCol.set(reverseFieldMap[this.sortBy] ?? null);
    this.tableSortDir.set(this.sortOrder as 'asc' | 'desc');

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
    this.filterAgeRating.set([]);
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
    this.activePresetName.set(null);
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
    this.activePresetName.set(this.translateService.instant(preset.labelKey));
    this.presetsSheetOpen.set(false);
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
      f.ageRating?.length ||
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
    this.filterAgeRating.set(f.ageRating ?? []);
    this.sortBy = f.sortBy ?? 'title';
    this.sortOrder = f.sortOrder ?? 'asc';
  }

  openSavePreset(): void {
    this.presetsSheetOpen.set(false);
    this.showSavePreset = !this.showSavePreset;
    if (this.showSavePreset) {
      this.filterOpen.set(true);
      const summary = this.filterSummary;
      const noFilterText = this.translateService.instant('animeList.filter.noFilterActive');
      this.newPresetName = summary !== noFilterText ? summary : '';
    }
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
      ageRating: this.filterAgeRating().length ? this.filterAgeRating() : undefined,
      sortBy: this.sortBy !== 'title' ? this.sortBy : undefined,
      sortOrder: this.sortOrder !== 'asc' ? this.sortOrder : undefined,
      activePresetName: this.activePresetName() ?? undefined,
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
      ageRating: this.filterAgeRating().length ? this.filterAgeRating() : undefined,
      sortBy: this.sortBy,
      sortOrder: this.sortOrder,
    };
  }

  private loadAnimes(reset: boolean, targetPage = 1): void {
    if (reset) {
      this.currentPage = targetPage;
      this.tablePage.set(targetPage);
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
      .getAnimes(this.currentPage, this.pageSize(), this.buildFilters())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.totalCount.set(data.totalCount);
          if (this.displayMode() === 4) {
            this.animes.set(data.data);
          } else {
            this.animes.update((existing) => [...existing, ...data.data]);
          }
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
      Math.max(0, elements.length - Math.floor(this.pageSize() / 2)),
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

  dismissCardHint(): void {
    localStorage.setItem('card-hint-v1', '1');
    this.cardHintActive.set(false);
  }

  openAnimeDetails(id: string): void {
    this.selectedAnimeId.set(id);
  }

  closeAnimeDetails(): void {
    this.selectedAnimeId.set(null);
  }
}

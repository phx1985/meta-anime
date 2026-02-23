import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  OnDestroy,
  QueryList,
  ViewChildren,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { trigger, state, style, animate, transition, query, stagger } from '@angular/animations';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SettingsService } from '../services/settings.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { AnimeDetailsResponse, AnimeFilters } from '../models/anime.models';
import { AnimeCardComponent } from './anime-card/anime-card.component';

const PAGE_SIZE = 20;
const DISPLAY_MODE_KEY = 'meta-anime:displayMode';

export type DisplayMode = 1 | 2 | 3;

export const GENRE_LIST = [
  'Action', 'Adventure', 'Avant Garde', 'Award Winning', 'Boys Love', 'Comedy', 'Drama',
  'Ecchi', 'Fantasy', 'Girls Love', 'Gourmet', 'Horror', 'Mystery', 'Romance', 'Sci-Fi',
  'Slice of Life', 'Sports', 'Supernatural', 'Suspense', 'Isekai', 'Mecha', 'Music',
  'Psychological', 'School', 'Seinen', 'Shoujo', 'Shounen', 'Space', 'Military', 'Historical',
];

const NSFW_GENRES = ['Ecchi', 'Hentai', 'Erotica', 'Boys Love', 'Girls Love'];
const SFW_GENRE_LIST = GENRE_LIST.filter(g => !NSFW_GENRES.includes(g));

export const WATCHING_STATES = [
  { value: 'watching',      labelKey: 'animeCard.malStatus.watching' },
  { value: 'completed',     labelKey: 'animeCard.malStatus.completed' },
  { value: 'on_hold',       labelKey: 'animeCard.malStatus.on_hold' },
  { value: 'dropped',       labelKey: 'animeCard.malStatus.dropped' },
  { value: 'plan_to_watch', labelKey: 'animeCard.malStatus.plan_to_watch' },
];

export const AIRING_STATES = [
  { value: 'finished_airing',  labelKey: 'animeCard.seriesStatus.finished_airing' },
  { value: 'currently_airing', labelKey: 'animeCard.seriesStatus.currently_airing' },
  { value: 'not_yet_aired',    labelKey: 'animeCard.seriesStatus.not_yet_aired' },
];

export const SORT_OPTIONS = [
  { value: 'title',         labelKey: 'animeList.sort.title' },
  { value: 'malscore',      labelKey: 'animeList.sort.malScore' },
  { value: 'myscore',       labelKey: 'animeList.sort.myScore' },
  { value: 'startdate',     labelKey: 'animeList.sort.startDate' },
  { value: 'enddate',       labelKey: 'animeList.sort.endDate' },
  { value: 'addeddate',     labelKey: 'animeList.sort.addedDate' },
  { value: 'sizeondisk',    labelKey: 'animeList.sort.sizeOnDisk' },
  { value: 'totalsizedisk', labelKey: 'animeList.sort.totalSizeDisk' },
];

export const CODEC_OPTIONS = [
  { value: 'hevc',  label: 'H.265 / HEVC' },
  { value: 'h264',  label: 'H.264 / AVC' },
  { value: 'av1',   label: 'AV1' },
  { value: 'mpeg2', label: 'MPEG-2' },
];

@Component({
  selector: 'app-anime-list',
  standalone: true,
  imports: [FormsModule, TranslatePipe, AnimeCardComponent],
  templateUrl: './anime-list.component.html',
  styleUrl: './anime-list.component.scss',
  animations: [
    trigger('gridLayout', [
      transition(':increment', [
        query('.anime-card', [
          style({ opacity: 0.7, transform: 'translateY(6px) scale(0.97)' }),
          stagger(5, [
            animate('300ms cubic-bezier(0.34, 1.4, 0.64, 1)', style({ opacity: 1, transform: 'none' })),
          ]),
        ], { optional: true }),
      ]),
    ]),
    trigger('filterCollapse', [
      state('open',   style({ height: '*',    opacity: 1, overflow: 'hidden' })),
      state('closed', style({ height: '0px', opacity: 0, overflow: 'hidden' })),
      transition('open <=> closed', animate('320ms cubic-bezier(0.4, 0, 0.2, 1)')),
    ]),
  ],
})
export class AnimeListComponent implements AfterViewInit, OnDestroy {
  private animeService    = inject(AnimeService);
  private settingsService = inject(SettingsService);
  private syncSignalR     = inject(SyncSignalRService);
  private destroyRef      = inject(DestroyRef);

  @ViewChildren('animeCard', { read: ElementRef }) cardRefs!: QueryList<ElementRef>;

  animes      = signal<AnimeDetailsResponse[]>([]);
  loading     = signal(false);
  loadingMore = signal(false);
  error       = signal(false);
  totalCount  = signal(0);
  allLoaded   = signal(false);
  displayMode = signal<DisplayMode>(2);
  layoutState = signal(0);
  searched    = signal(false);
  showNsfw    = signal(true);

  /** Whether the filter body is expanded */
  filterOpen  = signal(true);

  /** Whether the genre dropdown is open */
  genreDropdownOpen = false;

  // ── Filter state ──────────────────────────────────────────────────────────
  filterTitle        = '';
  filterGenre:        string[] = [];
  genreOperator: 'or' | 'and' = 'or';
  filterWatchState:   string[] = [];
  filterAiringStatus: string[] = [];
  filterHasFile: 'any' | 'yes' | 'no' = 'any';
  filterCodec:        string[] = [];
  sortBy             = 'title';
  sortOrder: 'asc' | 'desc' = 'asc';

  readonly watchingStates = WATCHING_STATES;
  readonly airingStates   = AIRING_STATES;
  readonly sortOptions    = SORT_OPTIONS;
  readonly codecOptions   = CODEC_OPTIONS;

  /** Genre list filtered by NSFW setting */
  readonly filteredGenreList = computed(() =>
    this.showNsfw() ? GENRE_LIST : SFW_GENRE_LIST
  );

  /** Label shown on the genre dropdown trigger button */
  readonly genreLabel = computed(() => {
    const sel = this.filterGenre;
    if (sel.length === 0) return null;
    if (sel.length <= 2) return sel.join(', ');
    return `${sel[0]}, ${sel[1]} +${sel.length - 2}`;
  });

  /** Count of active (non-default) filters for the badge */
  readonly activeFilterCount = computed(() => {
    let n = 0;
    if (this.filterTitle) n++;
    if (this.filterGenre.length) n++;
    if (this.filterWatchState.length) n++;
    if (this.filterAiringStatus.length) n++;
    if (this.filterHasFile !== 'any') n++;
    if (this.filterCodec.length) n++;
    return n;
  });

  private currentPage = 1;
  private scrollObserver: IntersectionObserver | null = null;

  constructor() {
    const saved = localStorage.getItem(DISPLAY_MODE_KEY);
    const n = Number(saved);
    if (saved && (n === 1 || n === 2 || n === 3)) this.displayMode.set(n as DisplayMode);

    this.settingsService.getShowNsfw()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(show => this.showNsfw.set(show));

    this.syncSignalR.syncCompleted$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => { if (this.searched()) this.loadAnimes(true); });
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

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    // Close genre dropdown if click is outside the genre-dropdown wrapper
    const target = event.target as HTMLElement;
    if (this.genreDropdownOpen && !target.closest('.genre-dropdown')) {
      this.genreDropdownOpen = false;
    }
  }

  // ── Display mode ──────────────────────────────────────────────────────────

  setDisplayMode(mode: DisplayMode): void {
    if (this.displayMode() === mode) return;
    this.displayMode.set(mode);
    this.layoutState.update(v => v + 1);
    localStorage.setItem(DISPLAY_MODE_KEY, String(mode));
  }

  // ── Filter panel collapse ─────────────────────────────────────────────────

  toggleFilterPanel(): void {
    this.filterOpen.update(v => !v);
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
    return current.includes(value)
      ? current.filter(v => v !== value)
      : [...current, value];
  }

  // ── Genre dropdown ────────────────────────────────────────────────────────

  toggleGenreDropdown(event: MouseEvent): void {
    event.stopPropagation(); // prevent document:click from immediately closing
    this.genreDropdownOpen = !this.genreDropdownOpen;
  }

  toggleGenre(genre: string, event: MouseEvent): void {
    event.stopPropagation();
    this.filterGenre = this.togglePill(this.filterGenre, genre);
    // Auto-reset operator to OR when ≤ 1 genre selected (operator toggle is hidden)
    if (this.filterGenre.length <= 1) this.genreOperator = 'or';
  }

  clearGenres(event: MouseEvent): void {
    event.stopPropagation();
    this.filterGenre = [];
  }

  // ── Search / Reset ────────────────────────────────────────────────────────

  search(): void {
    this.searched.set(true);
    this.filterOpen.set(false);   // collapse filter panel on search
    this.genreDropdownOpen = false;
    this.loadAnimes(true);
  }

  reset(): void {
    this.filterTitle        = '';
    this.filterGenre        = [];
    this.genreOperator      = 'or';
    this.filterWatchState   = [];
    this.filterAiringStatus = [];
    this.filterHasFile      = 'any';
    this.filterCodec        = [];
    this.sortBy             = 'title';
    this.sortOrder          = 'asc';
    this.searched.set(false);
    this.filterOpen.set(true);
    this.genreDropdownOpen  = false;
    this.animes.set([]);
    this.totalCount.set(0);
    this.allLoaded.set(false);
    this.error.set(false);
  }

  // ── Private ───────────────────────────────────────────────────────────────

  private resolveHasFile(): boolean | undefined {
    if (this.filterHasFile === 'yes') return true;
    if (this.filterHasFile === 'no') return false;
    return undefined;
  }

  private buildFilters(): AnimeFilters {
    return {
      title:         this.filterTitle        || undefined,
      genre:         this.filterGenre.length        ? this.filterGenre        : undefined,
      genreOperator: this.filterGenre.length > 1    ? this.genreOperator      : undefined,
      status:        this.filterWatchState.length   ? this.filterWatchState   : undefined,
      airingStatus:  this.filterAiringStatus.length ? this.filterAiringStatus : undefined,
      hasFile:       this.resolveHasFile(),
      codec:         this.filterCodec.length        ? this.filterCodec        : undefined,
      sortBy:        this.sortBy,
      sortOrder:     this.sortOrder,
    };
  }

  private loadAnimes(reset: boolean): void {
    if (reset) {
      this.currentPage = 1;
      this.animes.set([]);
      this.allLoaded.set(false);
      this.loading.set(true);
    } else {
      this.loadingMore.set(true);
    }
    this.error.set(false);

    this.animeService.getAnimes(this.currentPage, PAGE_SIZE, this.buildFilters())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => {
          this.totalCount.set(data.totalCount);
          this.animes.update(existing => [...existing, ...data.data]);
          this.allLoaded.set(!data.hasNextPage);
          this.loading.set(false);
          this.loadingMore.set(false);
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
          this.loadingMore.set(false);
        }
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
      Math.max(0, elements.length - Math.floor(PAGE_SIZE / 2))
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
      { threshold: 0 }
    );
    this.scrollObserver.observe(targetEl);
  }
}

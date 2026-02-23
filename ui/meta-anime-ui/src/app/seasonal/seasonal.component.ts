import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  QueryList,
  ViewChildren,
  inject,
  signal,
  computed,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SettingsService } from '../services/settings.service';
import { SeasonalAnimeItem, SeasonName } from '../models/anime.models';
import { SeasonalCardComponent } from './seasonal-card/seasonal-card.component';

const PAGE_SIZE = 20;
const SEASONS_PER_LOAD = 4;

export interface SeasonTab {
  year: number;
  season: SeasonName;
  labelKey: string;
}

/** Returns the current season name based on the month. */
function getCurrentSeason(month: number): SeasonName {
  if (month >= 1 && month <= 3) return 'winter';
  if (month >= 4 && month <= 6) return 'spring';
  if (month >= 7 && month <= 9) return 'summer';
  return 'fall';
}

/** Builds N seasons starting from the given year/seasonIdx, going back in time. */
function buildSeasons(count: number, fromYear: number, fromSeasonIdx: number): SeasonTab[] {
  const seasons: SeasonName[] = ['winter', 'spring', 'summer', 'fall'];
  const seasonLabelKeys: Record<SeasonName, string> = {
    winter: 'seasonal.season.winter',
    spring: 'seasonal.season.spring',
    summer: 'seasonal.season.summer',
    fall:   'seasonal.season.fall',
  };

  let year = fromYear;
  let seasonIdx = fromSeasonIdx;
  const result: SeasonTab[] = [];

  for (let i = 0; i < count; i++) {
    result.push({ year, season: seasons[seasonIdx], labelKey: seasonLabelKeys[seasons[seasonIdx]] });
    seasonIdx--;
    if (seasonIdx < 0) {
      seasonIdx = 3;
      year--;
    }
  }
  return result;
}

@Component({
  selector: 'app-seasonal',
  standalone: true,
  imports: [TranslatePipe, SeasonalCardComponent],
  templateUrl: './seasonal.component.html',
  styleUrl: './seasonal.component.scss',
})
export class SeasonalComponent implements AfterViewInit, OnDestroy {
  private animeService   = inject(AnimeService);
  private settingsService = inject(SettingsService);
  private destroyRef     = inject(DestroyRef);

  @ViewChildren('seasonalCard', { read: ElementRef }) cardRefs!: QueryList<ElementRef>;

  // Determine starting point once
  private readonly _now = new Date();
  private readonly _initYear = this._now.getFullYear();
  private readonly _initSeasonIdx: number;

  /** All currently loaded season tabs (starts with 4, grows by SEASONS_PER_LOAD). */
  tabs = signal<SeasonTab[]>([]);

  /** Whether there could be more seasons to load (earliest anime is ~1963). */
  readonly canLoadMoreSeasons = computed(() => {
    const last = this.tabs()[this.tabs().length - 1];
    return last ? last.year > 1963 : false;
  });

  activeTab    = signal<SeasonTab | null>(null);
  animes       = signal<SeasonalAnimeItem[]>([]);
  loading      = signal(false);
  loadingMore  = signal(false);
  error        = signal(false);
  allLoaded    = signal(false);
  showNsfw     = signal(false);

  private currentPage = 1;
  private scrollObserver: IntersectionObserver | null = null;
  private _nextSeasonIdx: number;
  private _nextYear: number;

  constructor() {
    const seasons: SeasonName[] = ['winter', 'spring', 'summer', 'fall'];
    this._initSeasonIdx = seasons.indexOf(getCurrentSeason(this._now.getMonth() + 1));
    this._nextSeasonIdx = this._initSeasonIdx;
    this._nextYear      = this._initYear;

    // Load initial 4 tabs
    this._appendSeasons(SEASONS_PER_LOAD);

    // Fetch NSFW setting
    this.settingsService.getShowNsfw()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this.showNsfw.set(v));
  }

  ngAfterViewInit(): void {
    this.cardRefs.changes
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.setupScrollSentinel());
  }

  ngOnDestroy(): void {
    this.scrollObserver?.disconnect();
  }

  selectTab(tab: SeasonTab): void {
    const active = this.activeTab();
    if (active && active.year === tab.year && active.season === tab.season) return;
    this.activeTab.set(tab);
    this.loadSeason(tab, true);
  }

  loadMoreSeasons(): void {
    if (!this.canLoadMoreSeasons()) return;
    this._appendSeasons(SEASONS_PER_LOAD);
  }

  private _appendSeasons(count: number): void {
    const newTabs = buildSeasons(count, this._nextYear, this._nextSeasonIdx);
    const seasons: SeasonName[] = ['winter', 'spring', 'summer', 'fall'];
    // Advance the pointer past the newly appended tabs
    const last = newTabs[newTabs.length - 1];
    let idx = seasons.indexOf(last.season) - 1;
    let year = last.year;
    if (idx < 0) { idx = 3; year--; }
    this._nextSeasonIdx = idx;
    this._nextYear = year;

    this.tabs.update(existing => [...existing, ...newTabs]);

    // Auto-select first tab on initial load
    if (this.activeTab() === null && newTabs.length > 0) {
      this.activeTab.set(newTabs[0]);
      this.loadSeason(newTabs[0], true);
    }
  }

  private loadSeason(tab: SeasonTab, reset: boolean): void {
    if (reset) {
      this.currentPage = 1;
      this.animes.set([]);
      this.allLoaded.set(false);
      this.loading.set(true);
    } else {
      this.loadingMore.set(true);
    }
    this.error.set(false);

    this.animeService.getSeasonalAnimes(tab.year, tab.season, this.currentPage, PAGE_SIZE)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => {
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
    const tab = this.activeTab();
    if (!tab) return;
    this.currentPage++;
    this.loadSeason(tab, false);
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


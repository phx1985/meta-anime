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
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SeasonalAnimeItem, SeasonName } from '../models/anime.models';
import { SeasonalCardComponent } from './seasonal-card/seasonal-card.component';

const PAGE_SIZE = 20;

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

/** Builds the last N seasons in reverse chronological order (most recent first). */
function buildLastSeasons(count: number): SeasonTab[] {
  const seasons: SeasonName[] = ['winter', 'spring', 'summer', 'fall'];
  const seasonLabelKeys: Record<SeasonName, string> = {
    winter: 'seasonal.season.winter',
    spring: 'seasonal.season.spring',
    summer: 'seasonal.season.summer',
    fall:   'seasonal.season.fall',
  };

  const now = new Date();
  let year = now.getFullYear();
  let seasonIdx = seasons.indexOf(getCurrentSeason(now.getMonth() + 1));

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
  private animeService = inject(AnimeService);
  private destroyRef   = inject(DestroyRef);

  @ViewChildren('seasonalCard', { read: ElementRef }) cardRefs!: QueryList<ElementRef>;

  readonly tabs = buildLastSeasons(4);

  activeTab  = signal<SeasonTab>(this.tabs[0]);
  animes     = signal<SeasonalAnimeItem[]>([]);
  loading    = signal(false);
  loadingMore = signal(false);
  error      = signal(false);
  allLoaded  = signal(false);

  private currentPage = 1;
  private scrollObserver: IntersectionObserver | null = null;

  constructor() {
    this.loadSeason(this.tabs[0], true);
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
    if (this.activeTab().year === tab.year && this.activeTab().season === tab.season) return;
    this.activeTab.set(tab);
    this.loadSeason(tab, true);
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
    this.currentPage++;
    this.loadSeason(this.activeTab(), false);
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

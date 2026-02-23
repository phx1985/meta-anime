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
import { SyncSignalRService } from '../services/sync-signalr.service';
import { AnimeDetailsResponse } from '../models/anime.models';
import { AnimeCardComponent } from './anime-card/anime-card.component';

const PAGE_SIZE = 20;

export type DisplayMode = 1 | 2 | 3;

@Component({
  selector: 'app-anime-list',
  standalone: true,
  imports: [TranslatePipe, AnimeCardComponent],
  templateUrl: './anime-list.component.html',
  styleUrl: './anime-list.component.scss'
})
export class AnimeListComponent implements AfterViewInit, OnDestroy {
  private animeService = inject(AnimeService);
  private syncSignalR  = inject(SyncSignalRService);
  private destroyRef   = inject(DestroyRef);

  @ViewChildren('animeCard', { read: ElementRef }) cardRefs!: QueryList<ElementRef>;

  animes      = signal<AnimeDetailsResponse[]>([]);
  loading     = signal(true);
  loadingMore = signal(false);
  error       = signal(false);
  totalCount  = signal(0);
  allLoaded   = signal(false);
  displayMode = signal<DisplayMode>(2);

  private currentPage = 1;
  private scrollObserver: IntersectionObserver | null = null;

  constructor() {
    this.loadAnimes(true);

    // Auto-refresh when a sync completes while on this page
    this.syncSignalR.syncCompleted$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadAnimes(true));
  }

  ngAfterViewInit(): void {
    this.cardRefs.changes
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.setupScrollSentinel());
  }

  ngOnDestroy(): void {
    this.scrollObserver?.disconnect();
  }

  setDisplayMode(mode: DisplayMode): void {
    this.displayMode.set(mode);
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

    this.animeService.getAnimes(this.currentPage, PAGE_SIZE)
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

    // Trigger at 50% through the last page batch, clamped within bounds
    const sentinelIdx = Math.min(
      elements.length - 1,
      Math.max(0, elements.length - Math.floor(PAGE_SIZE / 2))
    );
    const targetEl = elements[sentinelIdx].nativeElement;

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

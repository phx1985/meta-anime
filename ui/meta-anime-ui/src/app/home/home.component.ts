import { ChangeDetectionStrategy, Component, computed, inject, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { AnimeStatisticsResponse } from '../models/anime.models';
import { GenreChartComponent } from '../genre-chart/genre-chart.component';
import { MissingStatsComponent } from '../missing-stats/missing-stats.component';
import { CountUpDirective } from '../directives/count-up.directive';
import { ProviderStateService } from '../services/provider-state.service';
import { AnimeImageGalleryComponent } from '../anime-image-gallery/anime-image-gallery.component';
import { ActivityStatsComponent } from './activity-stats/activity-stats.component';
import { UiCardComponent } from '../shared/ui/ui-card.component';
import { UiStatePanelComponent } from '../shared/ui/ui-state-panel/ui-state-panel.component';

@Component({
  selector: 'app-home',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TranslatePipe,
    GenreChartComponent,
    MissingStatsComponent,
    CountUpDirective,
    AnimeImageGalleryComponent,
    ActivityStatsComponent,
    UiCardComponent,
    UiStatePanelComponent,
  ],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent {
  private animeService = inject(AnimeService);
  private syncSignalR = inject(SyncSignalRService);
  private destroyRef = inject(DestroyRef);
  readonly providerState = inject(ProviderStateService);

  stats = signal<AnimeStatisticsResponse | null>(null);
  /** True when the statistics request failed — drives the error state + retry. */
  readonly loadError = signal(false);

  /** Array used to render floating particles via @for in the template */
  readonly particles = Array.from({ length: 14 });

  /** Number of provider counter cards currently visible (MAL is always shown). */
  readonly visibleProviderCount = computed(() =>
    1 + (this.providerState.plexEnabled() ? 1 : 0) + (this.providerState.sonarrEnabled() ? 1 : 0),
  );

  /** Bootstrap column class for the provider counter cards row. */
  readonly colClass = computed(() => {
    switch (this.visibleProviderCount()) {
      case 1: return 'col-12';
      case 2: return 'col-sm-6';
      default: return 'col-6 col-md-4';
    }
  });

  /** Skeleton card placeholders matching the visible counter-card geometry. */
  readonly skeletonCards = computed(() => Array.from({ length: this.visibleProviderCount() }));

  constructor() {
    this.loadStats();

    // Auto-refresh when a sync completes while on this page.
    // resetFirst=true tears down the @if block so directives are recreated
    // and the count-up animation always fires from 0 regardless of whether
    // the numeric values actually changed.
    this.syncSignalR.syncCompleted$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadStats(true));
  }

  private loadStats(resetFirst = false): void {
    if (resetFirst) this.stats.set(null);
    this.loadError.set(false);
    this.animeService
      .getStatistics()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => this.stats.set(data),
        error: () => {
          this.stats.set(null);
          this.loadError.set(true);
        },
      });
  }

  /** Retry handler for the error state. */
  retryStats(): void {
    this.loadStats();
  }

  onGapCountDecremented(gapType: string): void {
    const current = this.stats();
    if (!current) return;
    if (gapType === 'sonarrNotInMal') {
      this.stats.set({ ...current, sonarrNotInMal: Math.max(0, current.sonarrNotInMal - 1) });
    } else if (gapType === 'plexNotInMal') {
      this.stats.set({ ...current, plexNotInMal: Math.max(0, current.plexNotInMal - 1) });
    }
  }
}

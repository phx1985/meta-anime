import { ChangeDetectionStrategy, Component, inject, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { AnimeStatisticsResponse } from '../models/anime.models';
import { GenreChartComponent } from '../genre-chart/genre-chart.component';
import { MissingStatsComponent } from '../missing-stats/missing-stats.component';
import { CountUpDirective } from '../directives/count-up.directive';

@Component({
  selector: 'app-home',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, GenreChartComponent, MissingStatsComponent, CountUpDirective],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent {
  private animeService = inject(AnimeService);
  private syncSignalR = inject(SyncSignalRService);
  private destroyRef = inject(DestroyRef);

  stats = signal<AnimeStatisticsResponse | null>(null);

  /** Array used to render floating particles via @for in the template */
  readonly particles = Array.from({ length: 14 });

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
    this.animeService
      .getStatistics()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => this.stats.set(data),
        error: () => this.stats.set(null),
      });
  }
}

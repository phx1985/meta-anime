import { Component, inject, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { AnimeStatisticsResponse } from '../models/anime.models';
import { GenreChartComponent } from '../genre-chart/genre-chart.component';
import { MissingStatsComponent } from '../missing-stats/missing-stats.component';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [TranslatePipe, GenreChartComponent, MissingStatsComponent],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent {
  private animeService = inject(AnimeService);
  private syncSignalR  = inject(SyncSignalRService);
  private destroyRef   = inject(DestroyRef);

  stats = signal<AnimeStatisticsResponse | null>(null);

  /** Array used to render floating particles via @for in the template */
  readonly particles = Array.from({ length: 14 });

  constructor() {
    this.loadStats();

    // Auto-refresh when a sync completes while on this page
    this.syncSignalR.syncCompleted$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadStats());
  }

  private loadStats(): void {
    this.animeService.getStatistics()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => this.stats.set(data),
        error: () => this.stats.set(null)
      });
  }
}

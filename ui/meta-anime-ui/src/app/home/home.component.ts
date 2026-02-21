import { Component, inject, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { AnimeStatisticsResponse } from '../models/anime.models';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [TranslatePipe],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent {
  private animeService = inject(AnimeService);
  private destroyRef = inject(DestroyRef);

  stats = signal<AnimeStatisticsResponse | null>(null);
  totalAnimes = computed(() => {
    const s = this.stats();
    return s ? s.PlexAnime + s.MyAnimeListAnime : 0;
  });

  /** Array used to render floating particles via @for in the template */
  readonly particles = Array.from({ length: 12 });

  constructor() {
    this.animeService.getStatistics()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => this.stats.set(data),
        error: () => this.stats.set(null)
      });
  }
}

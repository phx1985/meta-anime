import { Component, inject, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { AnimeStatisticsResponse } from '../models/anime.models';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <div class="hero-section">
      <div class="hero-content">
        <h1 class="hero-title">{{ 'home.title' | translate }}</h1>
        <p class="hero-subtitle">{{ 'home.subtitle' | translate }}</p>
      </div>
    </div>

    <div class="container py-5">
      @if (stats()) {
        <div class="row g-4 mb-4">
          <div class="col-6 col-md-4 col-lg-2">
            <div class="stat-card">
              <div class="stat-value">{{ totalAnimes() }}</div>
              <div class="stat-label">{{ 'home.stats.total' | translate }}</div>
            </div>
          </div>
          <div class="col-6 col-md-4 col-lg-2">
            <div class="stat-card stat-card--matched">
              <div class="stat-value">{{ stats()!.MatchedAnime }}</div>
              <div class="stat-label">{{ 'home.stats.matched' | translate }}</div>
            </div>
          </div>
          <div class="col-6 col-md-4 col-lg-2">
            <div class="stat-card stat-card--plex">
              <div class="stat-value">{{ stats()!.PlexAnime }}</div>
              <div class="stat-label">{{ 'home.stats.plex' | translate }}</div>
            </div>
          </div>
          <div class="col-6 col-md-4 col-lg-2">
            <div class="stat-card stat-card--sonarr">
              <div class="stat-value">{{ stats()!.SonarrAnime }}</div>
              <div class="stat-label">{{ 'home.stats.sonarr' | translate }}</div>
            </div>
          </div>
          <div class="col-6 col-md-4 col-lg-2">
            <div class="stat-card stat-card--mal">
              <div class="stat-value">{{ stats()!.MyAnimeListAnime }}</div>
              <div class="stat-label">{{ 'home.stats.mal' | translate }}</div>
            </div>
          </div>
        </div>
        <p class="text-center text-muted">{{ 'home.stats.description' | translate }}</p>
      } @else {
        <div class="d-flex justify-content-center py-5">
          <div class="spinner-border" role="status">
            <span class="visually-hidden">Loading...</span>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .hero-section {
      min-height: 40vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: linear-gradient(135deg, var(--gradient-start), var(--gradient-end));
      animation: gradientShift 8s ease infinite;
      background-size: 200% 200%;
    }
    @keyframes gradientShift {
      0% { background-position: 0% 50%; }
      50% { background-position: 100% 50%; }
      100% { background-position: 0% 50%; }
    }
    .hero-content {
      text-align: center;
      padding: 2rem;
    }
    .hero-title {
      font-size: 3.5rem;
      font-weight: 700;
      color: var(--hero-text);
      text-shadow: 0 2px 8px rgba(0,0,0,0.3);
      margin-bottom: 1rem;
    }
    .hero-subtitle {
      font-size: 1.25rem;
      color: var(--hero-text);
      opacity: 0.85;
    }
    .stat-card {
      background: var(--card-bg);
      border: 1px solid var(--card-border);
      border-radius: 12px;
      padding: 1.5rem 1rem;
      text-align: center;
      transition: transform 0.2s ease, box-shadow 0.2s ease;
    }
    .stat-card:hover {
      transform: translateY(-4px);
      box-shadow: 0 8px 24px var(--card-shadow);
    }
    .stat-value {
      font-size: 2.5rem;
      font-weight: 700;
      color: var(--accent-primary);
    }
    .stat-label {
      font-size: 0.85rem;
      color: var(--text-muted);
      margin-top: 0.25rem;
    }
    .stat-card--matched .stat-value { color: #22c55e; }
    .stat-card--plex .stat-value { color: #e5a00d; }
    .stat-card--sonarr .stat-value { color: #3b82f6; }
    .stat-card--mal .stat-value { color: #ef4444; }
  `]
})
export class HomeComponent {
  private animeService = inject(AnimeService);
  private destroyRef = inject(DestroyRef);
  stats = signal<AnimeStatisticsResponse | null>(null);
  totalAnimes = computed(() => {
    const s = this.stats();
    return s ? s.PlexAnime + s.MyAnimeListAnime : 0;
  });

  constructor() {
    this.animeService.getStatistics()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => this.stats.set(data),
        error: () => this.stats.set(null)
      });
  }
}

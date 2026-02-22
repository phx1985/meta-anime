import { Component, Input, signal, inject, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnimeDetailsResponse, MalAnimeInfo, SonarrAnimeInfo } from '../../models/anime.models';

@Component({
  selector: 'app-anime-card',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './anime-card.component.html',
  styleUrl: './anime-card.component.scss',
})
export class AnimeCardComponent {
  @Input({ required: true }) anime!: AnimeDetailsResponse;

  private translate = inject(TranslateService);

  showAltTitles = signal(false);
  descExpanded = signal(false);

  // ── Computed helpers ──────────────────────────────────────────────────────

  get mal(): MalAnimeInfo | undefined {
    return this.anime.malAnimes?.[0];
  }

  get sonarr(): SonarrAnimeInfo | undefined {
    return this.anime.sonarrAnimes?.[0];
  }

  get plex() {
    return this.anime.plexAnimes?.[0];
  }

  /** Best-effort localised title based on current UI language */
  get localizedTitle(): string | undefined {
    const lang = this.translate.currentLang ?? 'en';
    const alt = this.mal?.alternativeTitles;
    if (!alt) return undefined;
    if (lang === 'ja' && alt.ja) return alt.ja;
    // For all other languages fall back to the English alt title
    if (alt.en && alt.en !== this.anime.title) return alt.en;
    return undefined;
  }

  get allAltTitles(): string[] {
    const alt = this.mal?.alternativeTitles;
    if (!alt) return [];
    const titles: string[] = [];
    if (alt.en) titles.push(alt.en);
    if (alt.ja) titles.push(alt.ja);
    if (alt.synonyms) titles.push(...alt.synonyms);
    return [...new Set(titles)].filter((t) => t !== this.anime.title);
  }

  get description(): string {
    return this.mal?.synopsis ?? this.anime.description ?? '';
  }

  get truncatedDescription(): string {
    const d = this.description;
    if (!d) return '';
    return this.descExpanded() || d.length <= 200 ? d : d.slice(0, 200) + '…';
  }

  get malScore(): number {
    return this.mal?.rating?.malMean ?? 0;
  }

  get myScore(): number {
    return this.mal?.rating?.myScore ?? 0;
  }

  get genres(): string[] {
    return this.mal?.genres ?? [];
  }

  get startDate(): string | undefined {
    return this.mal?.startDate;
  }

  get endDate(): string | undefined {
    return this.mal?.endDate;
  }

  get addedDate(): Date | undefined {
    return this.mal?.addedDate ? new Date(this.mal.addedDate) : undefined;
  }

  get malViewState(): string | undefined {
    return this.mal?.viewState;
  }

  get plexProgress(): { watched: number; total: number } | undefined {
    const p = this.plex?.progression;
    return p ? { watched: p.viewedEpisodes, total: p.totalEpisodes } : undefined;
  }

  get malProgress(): { watched: number; total: number } | undefined {
    const p = this.mal?.progression;
    return p ? { watched: p.episodesWatched, total: p.totalEpisodes } : undefined;
  }

  /** Size on disk in GB (localized unit label) */
  get sizeOnDisk(): string | undefined {
    const bytes = this.sonarr?.sizeOnDisk;
    if (!bytes) return undefined;
    const gb = bytes / 1_073_741_824;
    const lang = this.translate.currentLang ?? 'en';
    const unit = lang === 'fr' ? 'Go' : 'GB';
    return `${gb.toFixed(2)} ${unit}`;
  }

  get codecEntries(): { codec: string; count: number; pct: number; color: string }[] {
    const dist = this.sonarr?.codecDistribution;
    if (!dist) return [];
    const entries = Object.entries(dist);
    const total = entries.reduce((s, [, v]) => s + v, 0);
    if (total === 0) return [];
    return entries
      .sort((a, b) => b[1] - a[1])
      .map(([codec, count], i) => ({
        codec,
        count,
        pct: Math.round((count / total) * 100),
        color: CODEC_COLORS[i % CODEC_COLORS.length],
      }));
  }

  get malUrl(): string | undefined {
    return this.mal?.url;
  }

  get sonarrUrl(): string | undefined {
    return this.sonarr?.url;
  }

  get plexUrl(): string | undefined {
    return this.plex?.url;
  }

  /** Accent hue based on MAL mean score (0-10): 0→red (0°), 5→yellow (65°), 10→green (130°) */
  get scoreHue(): number {
    const s = Math.min(10, Math.max(0, this.malScore));
    return Math.round(s * 13);
  }

  /** Stars array for visual score (filled / half / empty) */
  starsFor(score: number): ('full' | 'half' | 'empty')[] {
    const normalized = Math.min(10, Math.max(0, score)) / 2; // to 5-star scale
    return Array.from({ length: 5 }, (_, i) => {
      const diff = normalized - i;
      if (diff >= 0.75) return 'full';
      if (diff >= 0.25) return 'half';
      return 'empty';
    });
  }

  translateViewState(state: string | undefined): string {
    if (!state) return '';
    const map: Record<string, string> = {
      watching: '▶',
      completed: '✓',
      on_hold: '⏸',
      dropped: '✕',
      plan_to_watch: '📋',
    };
    return map[state] ?? state;
  }

  toggleAltTitles(): void {
    this.showAltTitles.update((v) => !v);
  }

  toggleDesc(): void {
    this.descExpanded.update((v) => !v);
  }
}

const CODEC_COLORS = ['#6c63ff', '#3ecfcf', '#e5a00d', '#ef4444', '#22c55e', '#3b82f6', '#a855f7'];

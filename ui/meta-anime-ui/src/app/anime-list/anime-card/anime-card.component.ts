import { Component, Input, signal, inject, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnimeDetailsResponse, MalAnimeInfo, SonarrAnimeInfo } from '../../models/anime.models';

const TRUNCATE_AT = 180;

/** Circumference of the score SVG arc (2π × r, r=15) */
const ARC_CIRCUMFERENCE = 94.25;

/** Season extraction patterns applied to Sonarr / MAL title */
const SEASON_PATTERNS: Array<[RegExp, (m: RegExpMatchArray) => string]> = [
  [/\bfinal\s+season\b/i,                  () => 'Final'],
  [/\b(\d+)(st|nd|rd|th)\s+season\b/i,    (m) => `S${m[1]}`],
  [/\bseason\s+(\d+)\b/i,                  (m) => `S${m[1]}`],
  [/\bpart\s+(\d+)\b/i,                    (m) => `P${m[1]}`],
  [/\bspecials?\b/i,                       () => 'SP'],
  [/\bmovie\b/i,                           () => 'Movie'],
  [/\bova\b/i,                             () => 'OVA'],
  [/\bona\b/i,                             () => 'ONA'],
];

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
  showLightbox = signal(false);

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.showLightbox()) this.showLightbox.set(false);
  }

  // ── Data accessors ────────────────────────────────────────────────────────

  get mal(): MalAnimeInfo | undefined {
    return this.anime.malAnimes?.[0];
  }

  get sonarr(): SonarrAnimeInfo | undefined {
    return this.anime.sonarrAnimes?.[0];
  }

  get plex() {
    return this.anime.plexAnimes?.[0];
  }

  // ── Title / image ─────────────────────────────────────────────────────────

  get coverImageUrl(): string | undefined {
    return this.mal?.coverImageUrl;
  }

  /** Best-effort localised title based on current UI language */
  get displayTitle(): string {
    const lang = this.translate.currentLang ?? 'en';
    const alt = this.mal?.alternativeTitles;
    if (alt) {
      if (lang === 'ja' && alt.ja) return alt.ja;
      if (alt.en && alt.en !== this.anime.title) return alt.en;
    }
    return this.anime.title;
  }

  get subtitle(): string | undefined {
    // Show the original DB title as subtitle when a localized title is shown
    const dt = this.displayTitle;
    if (dt !== this.anime.title) return this.anime.title;
    // Otherwise show Japanese alt as subtitle
    const ja = this.mal?.alternativeTitles?.ja;
    return ja && ja !== dt ? ja : undefined;
  }

  get allAltTitles(): string[] {
    const alt = this.mal?.alternativeTitles;
    if (!alt) return [];
    const titles: string[] = [];
    if (alt.en) titles.push(alt.en);
    if (alt.ja) titles.push(alt.ja);
    if (alt.synonyms) titles.push(...alt.synonyms);
    return [...new Set(titles)].filter((t) => t !== this.displayTitle);
  }

  // ── Description ───────────────────────────────────────────────────────────

  get description(): string {
    return this.mal?.synopsis ?? this.anime.description ?? '';
  }

  /** Arc circumference constant for score badge SVG */
  readonly arcCircumference = ARC_CIRCUMFERENCE;

  get truncatedDescription(): string {
    const d = this.description;
    if (!d) return '';
    return this.descExpanded() || d.length <= TRUNCATE_AT ? d : d.slice(0, TRUNCATE_AT) + '…';
  }

  // ── Status ────────────────────────────────────────────────────────────────

  /** MAL list status (watching / completed / on_hold / dropped / plan_to_watch) */
  get malViewState(): string | undefined {
    return this.mal?.viewState;
  }

  /** MAL series airing status (finished_airing / currently_airing / not_yet_aired) */
  get seriesStatus(): string | undefined {
    return this.mal?.status;
  }

  viewStateLabel(state: string): string {
    const key = `animeCard.malStatus.${state}`;
    const t = this.translate.instant(key);
    return t !== key ? t : state;
  }

  seriesStatusLabel(status: string): string {
    const key = `animeCard.seriesStatus.${status}`;
    const t = this.translate.instant(key);
    return t !== key ? t : status.replace(/_/g, ' ');
  }

  // ── Genres ────────────────────────────────────────────────────────────────

  get genres(): string[] {
    return this.mal?.genres ?? [];
  }

  /** Translate a genre name — safe for names containing spaces */
  translateGenre(name: string): string {
    const key = `genres.${name}`;
    const t = this.translate.instant(key);
    return t !== key ? t : name;
  }

  // ── Scores ────────────────────────────────────────────────────────────────

  get malScore(): number {
    return this.mal?.rating?.malMean ?? 0;
  }

  get myScore(): number {
    return this.mal?.rating?.myScore ?? 0;
  }

  /** 0–100 percentage for the score arc (scaled from 0–10) */
  scorePct(score: number): number {
    return Math.round(Math.min(10, Math.max(0, score)) * 10);
  }

  // ── Dates ─────────────────────────────────────────────────────────────────

  get startDate(): string | undefined {
    return this.mal?.startDate;
  }

  get endDate(): string | undefined {
    return this.mal?.endDate;
  }

  get addedDate(): Date | undefined {
    const raw = this.mal?.addedDate;
    if (!raw) return undefined;
    const d = new Date(raw);
    return d.getFullYear() > 1 ? d : undefined;
  }

  // ── Progression ───────────────────────────────────────────────────────────

  get plexProgress(): { watched: number; total: number } | undefined {
    const p = this.plex?.progression;
    return p ? { watched: p.viewedEpisodes, total: p.totalEpisodes } : undefined;
  }

  get malProgress(): { watched: number; total: number } | undefined {
    const p = this.mal?.progression;
    return p && p.totalEpisodes > 0 ? { watched: p.episodesWatched, total: p.totalEpisodes } : undefined;
  }

  // ── Storage / codec ───────────────────────────────────────────────────────

  /** Size on disk with localized unit (GB / Go) */
  get sizeOnDisk(): string | undefined {
    const bytes = this.sonarr?.sizeOnDisk;
    if (!bytes) return undefined;
    const gb = bytes / 1_073_741_824;
    const unit = this.translate.currentLang === 'fr' ? 'Go' : 'GB';
    return `${gb.toFixed(1)} ${unit}`;
  }

  /** Primary codec summary (top 2 codecs) */
  get codecSummary(): string | undefined {
    const dist = this.sonarr?.codecDistribution;
    if (!dist) return undefined;
    return Object.entries(dist)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 2)
      .map(([c]) => c)
      .join(' · ') || undefined;
  }

  /** Codec distribution as segments for the mini bar chart */
  get codecBars(): Array<{ name: string; pct: number; count: number }> {
    const dist = this.sonarr?.codecDistribution;
    if (!dist) return [];
    const entries = Object.entries(dist)
      .filter(([, v]) => v > 0)
      .sort((a, b) => b[1] - a[1]);
    const total = entries.reduce((s, [, v]) => s + v, 0);
    if (total === 0) return [];
    return entries.slice(0, 5).map(([name, count]) => ({
      name,
      pct: Math.round((count / total) * 100),
      count,
    }));
  }

  // ── URLs ──────────────────────────────────────────────────────────────────

  get malUrl(): string | null {
    return this.mal?.url ?? null;
  }

  get sonarrUrl(): string | null {
    return this.sonarr?.url ?? null;
  }

  get plexUrl(): string | null {
    return this.plex?.url ?? null;
  }

  // ── Interaction ───────────────────────────────────────────────────────────

  toggleAltTitles(): void {
    this.showAltTitles.update((v) => !v);
  }

  toggleDesc(): void {
    this.descExpanded.update((v) => !v);
  }

  openLightbox(): void {
    if (this.coverImageUrl) this.showLightbox.set(true);
  }

  closeLightbox(): void {
    this.showLightbox.set(false);
  }

  // ── Season tag ────────────────────────────────────────────────────────────

  /** Short season/type tag extracted from Sonarr or MAL title (e.g. "S2", "Final", "OVA") */
  get seasonTag(): string | undefined {
    const candidate = this.sonarr?.title ?? this.mal?.alternativeTitles?.en ?? this.anime.title;
    for (const [pattern, formatter] of SEASON_PATTERNS) {
      const m = candidate.match(pattern);
      if (m) return formatter(m);
    }
    return undefined;
  }
}

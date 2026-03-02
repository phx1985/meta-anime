import { ChangeDetectionStrategy, Component, Input, signal, inject, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnimeDetailsResponse, MalAnimeInfo, SonarrAnimeInfo } from '../../models/anime.models';
import { ScoreBadgeComponent } from './score-badge/score-badge.component';
import { GENRE_LIST, THEME_LIST, DEMOGRAPHIC_LIST } from '../../constants/genre-lists';
import { PopupService } from '../../services/popup.service';
import { PlexDeepLinkService } from '../../services/plex-deep-link.service';
import { FranchiseConstellationService } from '../../services/franchise-constellation.service';

@Component({
  selector: 'app-anime-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe, ScoreBadgeComponent],
  templateUrl: './anime-card.component.html',
  styleUrl: './anime-card.component.scss',
})
export class AnimeCardComponent {
  @Input({ required: true }) anime!: AnimeDetailsResponse;
  /** Genres actively selected in the parent filter — used to highlight matching tags. */
  @Input() activeGenres: string[] = [];
  @Input() activeThemes: string[] = [];
  @Input() activeDemographics: string[] = [];
  /** Whether the Plex provider is enabled globally (controls visibility of Plex action). */
  @Input() plexEnabled = true;
  /** Whether the Sonarr provider is enabled globally (controls visibility of Sonarr action). */
  @Input() sonarrEnabled = true;

  private translate = inject(TranslateService);
  private popupService = inject(PopupService);
  private plexDeepLink = inject(PlexDeepLinkService);
  protected franchiseConstellation = inject(FranchiseConstellationService);

  showAltTitles = signal(false);
  showLightbox = signal(false);

  // ── Local MAL overrides (updated immediately after a successful quick-update) ─
  /** Overridden view-state (e.g. 'watching', 'completed') after a quick-update. */
  private localMalStatus = signal<string | null>(null);
  /** Overridden episodes-watched count after a quick-update. */
  private localMalEpisodes = signal<number | null>(null);
  /** Overridden personal score after a quick-update. */
  private localMalScore = signal<number | null>(null);

  /** `true` while the neon-glow success animation is playing. */
  neonGlow = signal(false);

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

  get isTruncated(): boolean {
    return this.description.length > 180;
  }

  // ── Status ────────────────────────────────────────────────────────────────

  /** MAL list status (watching / completed / on_hold / dropped / plan_to_watch) */
  get malViewState(): string | undefined {
    return this.localMalStatus() ?? this.mal?.viewState;
  }

  /** MAL series airing status (finished_airing / currently_airing / not_yet_aired) */
  get seriesStatus(): string | undefined {
    return this.mal?.status;
  }

  /** MAL media type (tv / ova / movie / special / ona / music / unknown) */
  get mediaType(): string | undefined {
    return this.mal?.mediaType;
  }

  mediaTypeLabel(type: string): string {
    const key = `animeCard.mediaType.${type}`;
    const t = this.translate.instant(key);
    return t !== key ? t : type.toUpperCase();
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

  private get allMalGenres(): string[] {
    return this.mal?.genres ?? [];
  }

  /** MAL genre-category entries only. */
  get pureGenres(): string[] {
    return this.allMalGenres.filter((g) => GENRE_LIST.includes(g));
  }

  /** MAL theme entries only. */
  get themes(): string[] {
    return this.allMalGenres.filter((g) => THEME_LIST.includes(g));
  }

  /** MAL demographic entries only. */
  get demographics(): string[] {
    return this.allMalGenres.filter((g) => DEMOGRAPHIC_LIST.includes(g));
  }

  /** @deprecated Use pureGenres — kept for any existing template references. */
  get genres(): string[] {
    return this.allMalGenres;
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
    return this.localMalScore() ?? this.mal?.rating?.myScore ?? 0;
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
    const localEps = this.localMalEpisodes();
    const p = this.mal?.progression;
    if (!p || p.totalEpisodes <= 0) return undefined;
    const watched = localEps !== null ? localEps : p.episodesWatched;
    return { watched, total: p.totalEpisodes };
  }

  // ── Storage / codec ───────────────────────────────────────────────────────

  /** Size on disk with localized unit (GB / Go) */
  get sizeOnDisk(): string | undefined {
    const bytes = this.sonarr?.sizeOnDisk;
    if (!bytes) return undefined;
    const gb = bytes / 1_073_741_824;
    const unit = this.translate.instant('animeCard.sizeUnit');
    return `${gb.toFixed(1)} ${unit}`;
  }

  /** Fixed 3-segment codec breakdown: H.265 / H.264 / Other — null when no files.
   *  Segments with 0% are omitted so only meaningful codecs are shown. */
  get codecSegments(): Array<{ label: string; pct: number }> | null {
    const dist = this.sonarr?.codecDistribution;
    if (!dist) return null;
    const total = Object.values(dist).reduce((s, v) => s + v, 0);
    if (total === 0) return null;
    let h265 = 0,
      h264 = 0,
      other = 0;
    for (const [codec, count] of Object.entries(dist)) {
      const c = codec.toLowerCase();
      // H.265 aliases: hevc, h265, h.265, x265, x.265
      if (c === 'hevc' || c === 'h265' || c === 'h.265' || c === 'x265' || c === 'x.265')
        h265 += count;
      // H.264 aliases: avc, h264, h.264, x264, x.264, avc1
      else if (
        c === 'avc' ||
        c === 'h264' ||
        c === 'h.264' ||
        c === 'x264' ||
        c === 'x.264' ||
        c === 'avc1'
      )
        h264 += count;
      else other += count;
    }
    const pctH265 = Math.round((h265 / total) * 100);
    const pctH264 = Math.round((h264 / total) * 100);
    const pctOther = 100 - pctH265 - pctH264; // absorb rounding so segments always sum to 100
    // Filter out 0% segments — only show codecs that are actually present
    return [
      { label: this.translate.instant('animeCard.codec.h265'), pct: pctH265 },
      { label: this.translate.instant('animeCard.codec.h264'), pct: pctH264 },
      { label: this.translate.instant('animeCard.codec.other'), pct: pctOther },
    ].filter((s) => s.pct > 0);
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

  openSynopsisModal(): void {
    this.popupService.openSynopsis(this.displayTitle, this.description);
  }

  openLightbox(): void {
    if (this.coverImageUrl) this.showLightbox.set(true);
  }

  closeLightbox(): void {
    this.showLightbox.set(false);
  }

  openPlexLink(event: Event): void {
    if (!this.plexUrl) return;
    event.preventDefault();
    this.plexDeepLink.openLink(this.plexUrl);
  }

  openMalQuickUpdate(): void {
    const malId = this.mal?.id;
    if (!malId) return;
    this.popupService.openMalQuickUpdate({
      malId,
      title: this.displayTitle,
      coverImageUrl: this.coverImageUrl,
      malUrl: this.malUrl ?? undefined,
      currentStatus: this.mal?.viewState,
      currentEpisodes: this.localMalEpisodes() ?? this.mal?.progression?.episodesWatched,
      currentScore: this.localMalScore() ?? this.mal?.rating?.myScore,
      totalEpisodes: this.mal?.progression?.totalEpisodes,
      seriesStatus: this.mal?.status,
      onSaveSuccess: (status, episodes, score) => {
        this.localMalStatus.set(status);
        this.localMalEpisodes.set(episodes);
        this.localMalScore.set(score > 0 ? score : null);
        this.neonGlow.set(true);
        setTimeout(() => this.neonGlow.set(false), 2200);
      },
    });
  }

  openFranchiseConstellation(): void {
    const malId = this.mal?.id;
    if (!malId) return;
    this.franchiseConstellation.open(malId, this.displayTitle, this.coverImageUrl);
  }
}

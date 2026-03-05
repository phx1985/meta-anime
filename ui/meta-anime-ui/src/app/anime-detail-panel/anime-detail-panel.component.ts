import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  HostListener,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { DatePipe, TitleCasePipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { switchMap, tap, catchError } from 'rxjs/operators';
import { of } from 'rxjs';
import { trigger, transition, style, animate } from '@angular/animations';
import { AnimeDetailsResponse, MalAnimeInfo, SonarrAnimeInfo, SeasonalAnimeItem } from '../models/anime.models';
import { AnimeService } from '../services/anime.service';
import { SafePipe } from './safe.pipe';
import { ScoreBadgeComponent } from '../anime-list/anime-card/score-badge/score-badge.component';
import { GENRE_LIST, THEME_LIST, DEMOGRAPHIC_LIST } from '../constants/genre-lists';
import { PopupService } from '../services/popup.service';
import { PlexDeepLinkService } from '../services/plex-deep-link.service';
import { FranchiseConstellationService } from '../services/franchise-constellation.service';
import { AnimeImageGalleryService } from '../services/anime-image-gallery.service';
import { ProviderStateService } from '../services/provider-state.service';

@Component({
  selector: 'app-anime-detail-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, DatePipe, TitleCasePipe, SafePipe, ScoreBadgeComponent],
  templateUrl: './anime-detail-panel.component.html',
  styleUrl: './anime-detail-panel.component.scss',
  animations: [
    trigger('fadeIn', [
      transition(':enter', [style({ opacity: 0 }), animate('200ms ease-out', style({ opacity: 1 }))]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
    trigger('slideIn', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('260ms cubic-bezier(0.25, 0.46, 0.45, 0.94)', style({ transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
  ],
})
export class AnimeDetailPanelComponent {
  readonly animeId = input<string | null>(null);
  /** MAL ID to use when `animeId` is not set — fetches via the by-mal endpoint. */
  readonly malId = input<string | null>(null);
  readonly closed = output<void>();
  /** Fallback Sonarr base URL for the action button when per-anime URL is missing. */
  readonly sonarrBaseUrl = input<string | null>(null);
  /** Seasonal item for the add-to-providers button. Only provided from the seasonal page. */
  readonly seasonalItem = input<SeasonalAnimeItem | null>(null);
  /** Emitted when the user clicks the "Add to Library" button in seasonal mode. */
  readonly addRequested = output<void>();

  get showAddButton(): boolean {
    const s = this.seasonalItem();
    return s !== null && (!s.inMal || !s.inSonarr);
  }

  get isTrackedOnMal(): boolean {
    return !!(this.seasonalItem()?.inMal);
  }

  // ── Provider tracking — drives the tracking-status row in the panel ───────

  get trackedByMal(): boolean {
    const s = this.seasonalItem();
    return s !== null ? s.inMal : !!(this.mal?.id);
  }

  get trackedBySonarr(): boolean {
    const s = this.seasonalItem();
    return s !== null ? s.inSonarr : !!(this.sonarr);
  }

  get trackedByPlex(): boolean {
    const s = this.seasonalItem();
    return s !== null ? s.inPlex : !!(this.plex);
  }

  private animeService = inject(AnimeService);
  private destroyRef = inject(DestroyRef);
  private translate = inject(TranslateService);
  private popupService = inject(PopupService);
  private plexDeepLink = inject(PlexDeepLinkService);
  private franchiseConstellation = inject(FranchiseConstellationService);
  private gallery = inject(AnimeImageGalleryService);
  private providerState = inject(ProviderStateService);

  /** Expose provider state for template conditional rendering. */
  readonly plexEnabled = this.providerState.plexEnabled;
  readonly sonarrEnabled = this.providerState.sonarrEnabled;

  anime = signal<AnimeDetailsResponse | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);

  // ── AKA popover — click-toggled list of alternative titles (works on mobile) ──
  showAkaPopover = signal(false);
  toggleAkaPopover(): void { this.showAkaPopover.update(v => !v); }

  // ── Local MAL overrides — immediate feedback after quick-update ──────────
  private localMalStatus = signal<string | null>(null);
  private localMalEpisodes = signal<number | null>(null);
  private localMalScore = signal<number | null>(null);
  /** `true` while the neon-glow success animation plays after MAL save. */
  neonGlow = signal(false);

  constructor() {
    // Merge both id inputs: prefer animeId, fall back to malId-based lookup
    toObservable(this.animeId)
      .pipe(
        tap((id) => {
          const mal = this.malId();
          const isOpen = !!(id || mal);
          this.isLoading.set(isOpen);
          this.error.set(null);
          if (!isOpen) {
            this.anime.set(null);
            this.localMalStatus.set(null);
            this.localMalEpisodes.set(null);
            this.localMalScore.set(null);
            this.gallery.close();
          }
        }),
        switchMap((id) => {
          if (id) return this.animeService.getAnime(id).pipe(catchError(() => of(null)));
          const mal = this.malId();
          if (mal) return this.animeService.getAnimeByMalId(mal).pipe(catchError(() => of(null)));
          return of(null);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((data) => {
        this.anime.set(data);
        this.isLoading.set(false);
      });

    // React to malId changes when animeId is null
    toObservable(this.malId)
      .pipe(
        tap((mal) => {
          if (!this.animeId() && mal) {
            this.error.set(null);
            // Immediately populate from seasonal item data if available (no spinner).
            const seasonal = this.seasonalItem();
            if (seasonal) {
              this.anime.set(this.buildAnimeFromSeasonalItem(seasonal));
              this.isLoading.set(false);
            } else {
              this.isLoading.set(true);
            }
          } else if (!this.animeId() && !mal) {
            this.anime.set(null);
            this.localMalStatus.set(null);
            this.localMalEpisodes.set(null);
            this.localMalScore.set(null);
            this.gallery.close();
          }
        }),
        switchMap((mal) => {
          if (this.animeId()) return of(null); // animeId already handled above
          if (!mal) return of(null);
          // Still try to enrich from backend (more images, list status, etc.)
          return this.animeService.getAnimeByMalId(mal).pipe(catchError(() => of(null)));
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((data) => {
        if (!this.animeId()) {
          // Only replace with backend data if it returned something — keeps seasonal fallback on failure.
          if (data) this.anime.set(data);
          this.isLoading.set(false);
        }
      });
  }

  /** Builds a minimal AnimeDetailsResponse from a SeasonalAnimeItem so the panel
   *  can render immediately without waiting for a backend round-trip. */
  private buildAnimeFromSeasonalItem(item: SeasonalAnimeItem): AnimeDetailsResponse {
    const malInfo: MalAnimeInfo = {
      title: item.title,
      id: item.malId,
      url: item.malUrl,
      coverImageUrl: item.coverImageUrl,
      synopsis: item.synopsis,
      genres: item.genres,
      status: item.status,
      mediaType: item.mediaType,
      startDate: item.startDate,
      rating: { malMean: item.malScore, myScore: 0 },
      viewState: item.malViewState,
    };
    return {
      aniDbId: item.malId,
      title: item.title,
      description: item.synopsis,
      malAnimes: [malInfo],
      sonarrAnimes: [],
      plexAnimes: [],
    };
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.animeId() !== null || this.malId() !== null) this.close();
  }

  /** Track touch start X position for swipe-to-close gesture. */
  private touchStartX = 0;

  @HostListener('touchstart', ['$event'])
  onTouchStart(event: TouchEvent): void {
    this.touchStartX = event.touches[0].clientX;
  }

  @HostListener('touchend', ['$event'])
  onTouchEnd(event: TouchEvent): void {
    const deltaX = event.changedTouches[0].clientX - this.touchStartX;
    // Swipe right ≥ 60 px closes the panel
    if (deltaX >= 60) this.close();
  }

  close(): void {
    this.gallery.close();
    this.closed.emit();
  }

  // ── Data accessors ────────────────────────────────────────────────────────

  get mal(): MalAnimeInfo | undefined {
    return this.anime()?.malAnimes?.[0];
  }

  get sonarr(): SonarrAnimeInfo | undefined {
    return this.anime()?.sonarrAnimes?.[0];
  }

  get plex() {
    return this.anime()?.plexAnimes?.[0];
  }

  // ── Derived display values ────────────────────────────────────────────────

  get coverImageUrl(): string | undefined {
    return this.mal?.coverImageUrl;
  }

  get displayTitle(): string {
    const lang = this.translate.currentLang ?? 'en';
    const alt = this.mal?.alternativeTitles;
    if (alt) {
      if (lang === 'ja' && alt.ja) return alt.ja;
      if (alt.en && alt.en !== this.anime()?.title) return alt.en;
    }
    return this.anime()?.title ?? '';
  }

  get allAltTitles(): string[] {
    const dt = this.displayTitle;
    const alt = this.mal?.alternativeTitles;
    if (!alt) return [];
    const titles: string[] = [];
    if (alt.en) titles.push(alt.en);
    if (alt.ja) titles.push(alt.ja);
    if (alt.synonyms) titles.push(...alt.synonyms);
    return [...new Set(titles)].filter((t) => t !== dt);
  }

  get altTitlesTooltip(): string {
    return this.allAltTitles.join('\n');
  }

  /** MAL list status with local override after quick-update */
  get malViewState(): string | undefined {
    return this.localMalStatus() ?? this.mal?.viewState;
  }

  get sonarrUrl(): string | null {
    return this.sonarr?.url ?? this.sonarrBaseUrl() ?? null;
  }

  get plexUrl(): string | null {
    return this.plex?.url ?? null;
  }

  get malScore(): number {
    return this.mal?.rating?.malMean ?? 0;
  }

  get myScore(): number {
    return (this.localMalScore() ?? this.mal?.rating?.myScore) ?? 0;
  }

  // ── Genre / theme / demographic filtering ────────────────────────────────

  get pureGenres(): string[] {
    return this.mal?.genres?.filter((g) => GENRE_LIST.includes(g)) ?? [];
  }

  get themes(): string[] {
    return this.mal?.genres?.filter((g) => THEME_LIST.includes(g)) ?? [];
  }

  get demographics(): string[] {
    return this.mal?.genres?.filter((g) => DEMOGRAPHIC_LIST.includes(g)) ?? [];
  }

  // ── Codec breakdown — H.265 / H.264 / Other percentage segments ──────────

  /** Same logic as AnimeCard.codecSegments — percentage-based breakdown. */
  get codecSegments(): Array<{ label: string; pct: number }> | null {
    const dist = this.sonarr?.codecDistribution;
    if (!dist) return null;
    const total = Object.values(dist).reduce((s, v) => s + v, 0);
    if (total === 0) return null;
    let h265 = 0, h264 = 0, other = 0;
    for (const [codec, count] of Object.entries(dist)) {
      const c = codec.toLowerCase();
      if (c === 'hevc' || c === 'h265' || c === 'h.265' || c === 'x265' || c === 'x.265')
        h265 += count;
      else if (c === 'avc' || c === 'h264' || c === 'h.264' || c === 'x264' || c === 'x.264' || c === 'avc1')
        h264 += count;
      else other += count;
    }
    const pctH265 = Math.round((h265 / total) * 100);
    const pctH264 = Math.round((h264 / total) * 100);
    const pctOther = 100 - pctH265 - pctH264;
    return [
      { label: this.translate.instant('animeCard.codec.h265'), pct: pctH265 },
      { label: this.translate.instant('animeCard.codec.h264'), pct: pctH264 },
      { label: this.translate.instant('animeCard.codec.other'), pct: pctOther },
    ].filter((s) => s.pct > 0);
  }

  // ── Helpers ───────────────────────────────────────────────────────────────

  formatSizeOnDisk(bytes?: number): string {
    if (!bytes) return '';
    const gb = bytes / 1_073_741_824;
    return gb >= 1 ? `${gb.toFixed(1)} GB` : `${(bytes / 1_048_576).toFixed(0)} MB`;
  }

  // ── Actions ───────────────────────────────────────────────────────────────

  openMalQuickUpdate(): void {
    const malId = this.mal?.id;
    if (!malId) return;
    this.popupService.openMalQuickUpdate({
      malId,
      title: this.displayTitle,
      coverImageUrl: this.coverImageUrl,
      malUrl: this.mal?.url ?? undefined,
      currentStatus: this.localMalStatus() ?? this.mal?.viewState,
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

  openPlexLink(): void {
    if (!this.plexUrl) return;
    this.plexDeepLink.openLink(this.plexUrl, this.anime()?.title ?? '');
  }

  openFranchiseConstellation(): void {
    const malId = this.mal?.id;
    if (!malId) return;
    this.franchiseConstellation.open(malId, this.displayTitle, this.coverImageUrl);
  }

  openPosterGallery(): void {
    const mal = this.mal;
    if (!mal) return;
    const images = mal.pictures?.length
      ? mal.pictures
      : mal.coverImageUrl
        ? [mal.coverImageUrl]
        : [];
    if (images.length) this.gallery.open(images);
  }
}

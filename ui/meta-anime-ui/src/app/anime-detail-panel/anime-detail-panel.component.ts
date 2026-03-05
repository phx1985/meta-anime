import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  afterEveryRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { DatePipe, TitleCasePipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { switchMap, tap, catchError } from 'rxjs/operators';
import { of } from 'rxjs';
import { trigger, transition, style, animate } from '@angular/animations';
import { AnimeDetailsResponse, MalAnimeInfo, SonarrAnimeInfo, PlexAnimeInfo, SeasonalAnimeItem } from '../models/anime.models';
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
  /** Mirrors `PopupService.malQuickUpdateSaving` — drives the running-glow on the Quick Update button. */
  protected readonly malUpdating = this.popupService.malQuickUpdateSaving;

  /** Reference to the Quick Update button element — used by afterRender to measure real pixel dimensions. */
  private readonly malUpdateBtnRef = viewChild<ElementRef<HTMLButtonElement>>('malUpdateBtn');

  /**
   * Measured button dimensions, written by afterRender after every Angular render cycle.
   * Using a separate signal (rather than reading offsetWidth inside computed) guarantees
   * that the geometry is always derived from a post-layout DOM measurement.
   */
  private readonly _neonDims = signal({ w: 155, h: 38 });

  /**
   * All SVG geometry for the neon tracer, derived from the button's real pixel size.
   * Constant-speed formula: perimeter / 280 px·s⁻¹, clamped to [0.9s, 2.5s].
   */
  readonly neonGeometry = computed(() => {
    const { w, h } = this._neonDims();
    const perimeter = 2 * (w - h) + Math.PI * h;
    const cometLen  = Math.round(perimeter * 0.18);
    const sparkLen  = Math.round(perimeter * 0.035);
    const dur       = Math.min(2.5, Math.max(0.9, perimeter / 280));
    const delayB    = -((sparkLen / perimeter) * dur);
    return {
      viewBox:    `0 0 ${w} ${h}`,
      rx:         h / 2,
      w,
      h,
      perimeter:  Math.round(perimeter),
      dashA:      `${cometLen} ${Math.round(perimeter - cometLen)}`,
      dashB:      `${sparkLen} ${Math.round(perimeter - sparkLen)}`,
      animDur:    `${dur.toFixed(2)}s`,
      animDelayB: `${delayB.toFixed(3)}s`,
    };
  });

  constructor() {
    // Merge both id inputs: prefer animeId, fall back to malId-based lookup
    toObservable(this.animeId)
      .pipe(
        tap((id) => {
          const mal = this.malId();
          const isOpen = !!(id || mal);
          this.error.set(null);
          if (isOpen) {
            // Immediately populate from seasonal item data if available.
            // This prevents a blank panel when `getAnime(aniDbId)` returns 404
            // because the series was just added to Sonarr/MAL but hasn't been
            // synced into the local MetaAnime DB yet.
            const seasonal = this.seasonalItem();
            if (seasonal) {
              this.anime.set(this.buildAnimeFromSeasonalItem(seasonal));
              this.isLoading.set(false);
            } else if (!this.anime()) {
              // Only show loading spinner when we have nothing to display yet.
              // If anime() already has cached data, keep showing it rather than
              // flashing a skeleton every time the panel re-opens.
              this.isLoading.set(true);
            }
          } else {
            // NOTE: anime is intentionally NOT cleared here — clearing it immediately
            // would blank the panel during its 200ms slide-out animation.
            // It will be replaced when the panel next opens.
            this.isLoading.set(false);
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
        const seasonal = this.seasonalItem();
        if (data) {
          // Sonarr/Plex-only DB entries have no malAnimes. Supplement with seasonal
          // MAL data so the panel always shows cover image, synopsis, ratings, etc.
          if (!data.malAnimes?.length && seasonal) {
            const fromSeasonal = this.buildAnimeFromSeasonalItem(seasonal);
            this.anime.set({ ...fromSeasonal, ...data, malAnimes: fromSeasonal.malAnimes });
          } else {
            this.anime.set(data);
          }
        } else if (!this.anime()) {
          // Backend returned null (404) and there is no existing data yet.
          // Use the seasonal item as an immediate fallback so the panel is never empty.
          if (seasonal) this.anime.set(this.buildAnimeFromSeasonalItem(seasonal));
        }
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
            } else if (!this.anime()) {
              // Only show loading spinner when we have nothing to display yet.
              this.isLoading.set(true);
            }
          } else if (!this.animeId() && !mal) {
            // NOTE: anime is intentionally NOT cleared here — clearing it immediately
            // would blank the panel during its 200ms slide-out animation.
            this.isLoading.set(false);
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
          const seasonal = this.seasonalItem();
          if (data) {
            // Sonarr/Plex-only DB entries have no malAnimes. Supplement with seasonal
            // MAL data so the panel always shows cover image, synopsis, ratings, etc.
            if (!data.malAnimes?.length && seasonal) {
              const fromSeasonal = this.buildAnimeFromSeasonalItem(seasonal);
              this.anime.set({ ...fromSeasonal, ...data, malAnimes: fromSeasonal.malAnimes });
            } else {
              this.anime.set(data);
            }
          } else if (!this.anime()) {
            // Backend returned null (404) and there is no existing data yet.
            // Use the seasonal item as an immediate fallback so the panel is never empty.
            if (seasonal) this.anime.set(this.buildAnimeFromSeasonalItem(seasonal));
          }
          this.isLoading.set(false);
        }
      });

    // Safety-net: runs after every CD pass with guaranteed up-to-date signal values.
    // Handles the timing gap where tap()/subscribe() fired before the [seasonalItem]
    // input was propagated, leaving anime() null even though the panel is open.
    effect(() => {
      const isOpen = !!(this.animeId() || this.malId());
      const seasonal = this.seasonalItem();
      if (isOpen && seasonal) {
        untracked(() => {
          if (!this.anime()) {
            this.anime.set(this.buildAnimeFromSeasonalItem(seasonal));
            this.isLoading.set(false);
          }
        });
      }
    });

    // Measure the Quick Update button after every render so neonGeometry() always has
    // accurate pixel dimensions regardless of layout context (mobile vs PC).
    afterEveryRender(() => {
      const el = this.malUpdateBtnRef()?.nativeElement;
      if (!el || !this.malUpdating()) return;
      const w = el.offsetWidth;
      const h = el.offsetHeight;
      if (w > 0 && h > 0) {
        const cur = untracked(() => this._neonDims());
        if (cur.w !== w || cur.h !== h) {
          untracked(() => this._neonDims.set({ w, h }));
        }
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
    const sonarrAnimes: SonarrAnimeInfo[] = item.inSonarr
      ? [{ title: item.title, url: item.sonarrUrl, exists: true }]
      : [];
    const plexAnimes: PlexAnimeInfo[] = item.inPlex
      ? [{ title: item.title, url: item.plexUrl }]
      : [];
    return {
      aniDbId: item.malId,
      title: item.title,
      description: item.synopsis,
      malAnimes: [malInfo],
      sonarrAnimes: sonarrAnimes.length > 0 ? sonarrAnimes : undefined,
      plexAnimes: plexAnimes.length > 0 ? plexAnimes : undefined,
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

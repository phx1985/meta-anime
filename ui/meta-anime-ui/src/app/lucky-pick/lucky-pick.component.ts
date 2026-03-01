import {
  Component,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
  HostListener,
  Output,
  EventEmitter,
  ChangeDetectionStrategy,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { AnimeService } from '../services/anime.service';
import { UserPreferencesService } from '../services/user-preferences.service';
import { ProviderStateService } from '../services/provider-state.service';
import { PlexDeepLinkService } from '../services/plex-deep-link.service';
import { PopupService } from '../services/popup.service';
import { AnimeDetailsResponse, AnimeFilters, MalAnimeInfo } from '../models/anime.models';
import { ScoreBadgeComponent } from '../anime-list/anime-card/score-badge/score-badge.component';
import { GENRE_LIST, THEME_LIST, DEMOGRAPHIC_LIST } from '../constants/genre-lists';

type LuckyState = 'idle' | 'spinning' | 'result' | 'no-match' | 'error';

@Component({
  selector: 'app-lucky-pick',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, RouterLink, ScoreBadgeComponent],
  templateUrl: './lucky-pick.component.html',
  styleUrl: './lucky-pick.component.scss',
})
export class LuckyPickComponent implements OnInit, OnDestroy {
  @Output() closed = new EventEmitter<void>();

  private animeService = inject(AnimeService);
  protected userPrefs = inject(UserPreferencesService);
  private providerState = inject(ProviderStateService);
  private translate = inject(TranslateService);
  private plexDeepLink = inject(PlexDeepLinkService);
  private popupService = inject(PopupService);

  /** Minimum character count above which the synopsis is considered truncated. */
  private static readonly SYNOPSIS_TRUNCATE_THRESHOLD = 300;

  protected state = signal<LuckyState>('idle');
  protected result = signal<AnimeDetailsResponse | null>(null);

  /** Randomly-selected humorous button phrases, rerolled on every spin */
  protected plexPhrase      = signal(this.pickPhrase('luckyPick.phrases.plex'));
  protected malPhrase       = signal(this.pickPhrase('luckyPick.phrases.mal'));
  protected rerollPhrase    = signal(this.pickPhrase('luckyPick.phrases.reroll'));
  protected searchingPhrase = signal(this.pickPhrase('luckyPick.phrases.searching'));

  /** True if the OS prefers reduced motion */
  protected readonly prefersReducedMotion = this.checkReducedMotion();

  /** Expose provider state for template binding */
  protected get plexProviderEnabled(): boolean {
    return this.providerState.plexEnabled();
  }
  protected get sonarrProviderEnabled(): boolean {
    return this.providerState.sonarrEnabled();
  }

  private checkReducedMotion(): boolean {
    try {
      return typeof window !== 'undefined' &&
        typeof window.matchMedia === 'function'
        ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
        : false;
    } catch {
      return false;
    }
  }

  /** Spin duration in ms — reduced when prefersReducedMotion */
  private readonly SPIN_MS = this.prefersReducedMotion ? 0 : 2200;
  private spinTimer: ReturnType<typeof setTimeout> | null = null;
  private langSub: Subscription | null = null;

  protected isSpinning = computed(() => this.state() === 'spinning');
  protected isResult = computed(() => this.state() === 'result');
  protected isNoMatch = computed(() => this.state() === 'no-match');
  protected isError = computed(() => this.state() === 'error');

  // ── Getters for result card ───────────────────────────────────────────────

  protected get anime(): AnimeDetailsResponse | null {
    return this.result();
  }

  protected get coverImageUrl(): string | undefined {
    return this.anime?.malAnimes?.[0]?.coverImageUrl;
  }

  protected get title(): string {
    const a = this.anime;
    if (!a) return '';
    const en = a.malAnimes?.[0]?.alternativeTitles?.en;
    return en ?? a.title;
  }

  protected get synopsis(): string {
    const a = this.anime;
    if (!a) return '';
    return a.malAnimes?.[0]?.synopsis ?? a.description ?? '';
  }

  protected get isSynopsisTruncated(): boolean {
    return this.synopsis.length > LuckyPickComponent.SYNOPSIS_TRUNCATE_THRESHOLD;
  }

  protected openSynopsisModal(event?: Event): void {
    event?.preventDefault();
    this.popupService.openSynopsis(this.title, this.synopsis);
  }

  protected get malUrl(): string | null {
    return this.anime?.malAnimes?.[0]?.url ?? null;
  }

  protected get plexUrl(): string | null {
    return this.anime?.plexAnimes?.[0]?.url ?? null;
  }

  openPlexLink(event: Event): void {
    if (!this.plexUrl) return;
    event.preventDefault();
    this.plexDeepLink.openLink(this.plexUrl);
  }

  protected get mal(): MalAnimeInfo | undefined {
    return this.anime?.malAnimes?.[0];
  }

  // ── Genre / Theme / Demographic ──────────────────────────────────────────

  private get allMalGenres(): string[] {
    return this.mal?.genres ?? [];
  }

  protected get pureGenres(): string[] {
    return this.allMalGenres.filter((g) => GENRE_LIST.includes(g));
  }

  protected get themes(): string[] {
    return this.allMalGenres.filter((g) => THEME_LIST.includes(g));
  }

  protected get demographics(): string[] {
    return this.allMalGenres.filter((g) => DEMOGRAPHIC_LIST.includes(g));
  }

  // ── Status / Scores / Dates ──────────────────────────────────────────────

  protected get malViewState(): string | undefined {
    return this.mal?.viewState;
  }

  protected get seriesStatus(): string | undefined {
    return this.mal?.status;
  }

  protected get mediaType(): string | undefined {
    return this.mal?.mediaType;
  }

  protected get malScore(): number {
    return this.mal?.rating?.malMean ?? 0;
  }

  protected get startDate(): string | undefined {
    return this.mal?.startDate;
  }

  protected get endDate(): string | undefined {
    return this.mal?.endDate;
  }

  protected mediaTypeLabel(type: string): string {
    const key = `animeCard.mediaType.${type}`;
    const t = this.translate.instant(key);
    return t !== key ? t : type.toUpperCase();
  }

  protected viewStateLabel(state: string): string {
    const key = `animeCard.malStatus.${state}`;
    const t = this.translate.instant(key);
    return t !== key ? t : state;
  }

  protected seriesStatusLabel(status: string): string {
    const key = `animeCard.seriesStatus.${status}`;
    const t = this.translate.instant(key);
    return t !== key ? t : status.replace(/_/g, ' ');
  }

  protected translateGenre(name: string): string {
    const key = `genres.${name}`;
    const t = this.translate.instant(key);
    return t !== key ? t : name;
  }

  // ── Lifecycle ─────────────────────────────────────────────────────────────

  ngOnInit(): void {
    // Re-roll phrases when the user switches language so copy matches locale
    this.langSub = this.translate.onLangChange.subscribe(() => {
      this.plexPhrase.set(this.pickPhrase('luckyPick.phrases.plex'));
      this.malPhrase.set(this.pickPhrase('luckyPick.phrases.mal'));
      this.rerollPhrase.set(this.pickPhrase('luckyPick.phrases.reroll'));
      this.searchingPhrase.set(this.pickPhrase('luckyPick.phrases.searching'));
    });
    this.spin();
  }

  // ── Spin trigger ─────────────────────────────────────────────────────────

  spin(): void {
    if (this.state() === 'spinning') return;
    this.result.set(null);
    this.state.set('spinning');

    // Reroll funny phrases on every spin so the copy feels fresh
    this.plexPhrase.set(this.pickPhrase('luckyPick.phrases.plex'));
    this.malPhrase.set(this.pickPhrase('luckyPick.phrases.mal'));
    this.rerollPhrase.set(this.pickPhrase('luckyPick.phrases.reroll'));
    this.searchingPhrase.set(this.pickPhrase('luckyPick.phrases.searching'));

    const filters: AnimeFilters = {};

    if (this.userPrefs.luckyOnlyPlanToWatch()) {
      filters.status = ['plan_to_watch'];
    }

    if (this.userPrefs.luckyOnlyFinished()) {
      filters.airingStatus = ['finished_airing'];
    }

    // Only apply "has local files" filter when Sonarr is enabled;
    // when Sonarr is disabled there are no local file records to match against.
    if (this.userPrefs.luckyOnlyWithFiles() && this.providerState.sonarrEnabled()) {
      filters.hasFile = true;
    }

    // Fetch first page to know total count, then pick a random page/item
    this.animeService.getAnimes(1, 1, filters).subscribe({
      next: (firstPage) => {
        const total = firstPage.totalCount;
        if (total === 0) {
          this.finishSpin('no-match');
          return;
        }
        // Pick a random item index
        const randomIndex = Math.floor(Math.random() * total);
        const page = randomIndex + 1; // pageSize=1 → page N = item N
        this.animeService.getAnimes(page, 1, filters).subscribe({
          next: (page2) => {
            const picked = page2.data[0] ?? null;
            this.result.set(picked);
            this.finishSpin(picked ? 'result' : 'no-match');
          },
          error: () => this.finishSpin('error'),
        });
      },
      error: () => this.finishSpin('error'),
    });
  }

  private finishSpin(nextState: LuckyState): void {
    if (this.SPIN_MS > 0) {
      this.spinTimer = setTimeout(() => this.state.set(nextState), this.SPIN_MS);
    } else {
      this.state.set(nextState);
    }
  }

  close(): void {
    this.clearTimer();
    this.closed.emit();
  }

  tryAgain(): void {
    this.spin();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.close();
  }

  ngOnDestroy(): void {
    this.clearTimer();
    this.langSub?.unsubscribe();
  }

  private clearTimer(): void {
    if (this.spinTimer) {
      clearTimeout(this.spinTimer);
      this.spinTimer = null;
    }
  }

  /**
   * Uses TranslateService to load the phrase array for the given i18n key,
   * then returns one uniformly-random element from it.
   * Falls back gracefully when the key resolves to a non-array value.
   */
  private pickPhrase(key: string): string {
    const value: unknown = this.translate.instant(key);
    const arr: string[] = Array.isArray(value) ? (value as string[]) : [String(value)];
    return arr[Math.floor(Math.random() * arr.length)];
  }
}

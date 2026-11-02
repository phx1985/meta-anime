import {
  Component,
  ElementRef,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
  HostListener,
  ChangeDetectionStrategy,
  input,
  output,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { UserPreferencesService } from '../services/user-preferences.service';
import { ProviderStateService } from '../services/provider-state.service';
import { PlexDeepLinkService } from '../services/plex-deep-link.service';
import { LuckyFilterKey, LuckyPickService } from '../services/lucky-pick.service';
import { AnimeDetailsResponse, MalAnimeInfo } from '../models/anime.models';
import { ScoreBadgeComponent } from '../anime-list/anime-card/score-badge/score-badge.component';
import { GENRE_LIST, THEME_LIST, DEMOGRAPHIC_LIST } from '../constants/genre-lists';

@Component({
  selector: 'app-lucky-pick',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, RouterLink, ScoreBadgeComponent],
  templateUrl: './lucky-pick.component.html',
  styleUrl: './lucky-pick.component.scss',
})
export class LuckyPickComponent implements OnInit, OnDestroy {
  readonly closed = output<void>();
  /** Emits the MetaAnime internal ID (aniDbId) to open the side detail panel. */
  readonly animeSelected = output<string>();
  /** True while the anime detail panel is open above us — suppresses our Escape handler. */
  readonly detailPanelOpen = input<boolean>(false);

  protected userPrefs = inject(UserPreferencesService);
  protected providerState = inject(ProviderStateService);
  private translate = inject(TranslateService);
  private plexDeepLink = inject(PlexDeepLinkService);
  /** Shared roll engine — the Bot-kun in-chat card rolls through the same one. */
  protected lucky = inject(LuckyPickService);

  private readonly cardRef = viewChild<ElementRef<HTMLElement>>('card');

  /** Max tag chips before collapsing the rest into a "+N" indicator. */
  private static readonly MAX_TAG_CHIPS = 5;
  /** Character count above which the synopsis links out to the detail panel. */
  private static readonly SYNOPSIS_TRUNCATE_THRESHOLD = 300;

  protected readonly state = this.lucky.state;
  protected readonly result = this.lucky.result;
  protected readonly history = this.lucky.history;

  protected isSpinning = computed(() => this.state() === 'spinning');
  protected isResult = computed(() => this.state() === 'result');
  protected isNoMatch = computed(() => this.state() === 'no-match');
  protected isError = computed(() => this.state() === 'error');

  /** Randomised flavour copy — shown as a whisper line, never as a button label. */
  protected quip = signal('');
  protected searchingPhrase = signal('');
  private langSub: Subscription | null = null;

  protected readonly prefersReducedMotion = this.lucky.prefersReducedMotion;

  private previouslyFocused: HTMLElement | null = null;
  private previousBodyOverflow = '';

  // ── Display getters ────────────────────────────────────────────────────────
  protected get anime(): AnimeDetailsResponse | null {
    return this.result();
  }

  protected get coverImageUrl(): string | undefined {
    return this.anime?.malAnimes?.[0]?.coverImageUrl;
  }

  protected get title(): string {
    const a = this.anime;
    if (!a) return '';
    // alternativeTitles.en can be an empty string, which `??` would happily keep.
    return a.malAnimes?.[0]?.alternativeTitles?.en?.trim() || a.title;
  }

  protected get synopsis(): string {
    const a = this.anime;
    if (!a) return '';
    return a.malAnimes?.[0]?.synopsis ?? a.description ?? '';
  }

  protected get isSynopsisTruncated(): boolean {
    return this.synopsis.length > LuckyPickComponent.SYNOPSIS_TRUNCATE_THRESHOLD;
  }

  protected get mal(): MalAnimeInfo | undefined {
    return this.anime?.malAnimes?.[0];
  }

  protected get malUrl(): string | null {
    return this.mal?.url ?? null;
  }

  protected get plexUrl(): string | null {
    return this.anime?.plexAnimes?.[0]?.url ?? null;
  }

  /** Plex only earns the primary slot when the provider is on AND this title is there. */
  protected get showPlexAction(): boolean {
    return !!this.plexUrl && this.providerState.plexEnabled();
  }

  protected get malScore(): number {
    return this.mal?.rating?.malMean ?? 0;
  }

  protected get hasScore(): boolean {
    return !!this.mal?.rating;
  }

  protected get mediaType(): string | undefined {
    return this.mal?.mediaType;
  }

  protected get malViewState(): string | undefined {
    return this.mal?.viewState;
  }

  protected get seriesStatus(): string | undefined {
    return this.mal?.status;
  }

  protected get startDate(): string | undefined {
    return this.mal?.startDate;
  }

  protected get endDate(): string | undefined {
    return this.mal?.endDate;
  }

  /** Genres → themes → demographics, capped so the card never grows a scrollbar. */
  protected get tagChips(): { label: string; kind: 'genre' | 'theme' | 'demographic' }[] {
    const all = [
      ...this.pureGenres.map((g) => ({ label: this.translateGenre(g), kind: 'genre' as const })),
      ...this.themes.map((t) => ({ label: this.translateGenre(t), kind: 'theme' as const })),
      ...this.demographics.map((d) => ({ label: this.translateGenre(d), kind: 'demographic' as const })),
    ];
    return all.slice(0, LuckyPickComponent.MAX_TAG_CHIPS);
  }

  protected get hiddenTagCount(): number {
    const total = this.pureGenres.length + this.themes.length + this.demographics.length;
    return Math.max(0, total - LuckyPickComponent.MAX_TAG_CHIPS);
  }

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

  /** Announced by the visually-hidden aria-live region when a result lands. */
  protected liveMessage = computed(() => {
    const a = this.result();
    if (!a || this.state() !== 'result') return '';
    const score = this.malScore;
    return score > 0 ? `${this.title} — ${score}/10` : this.title;
  });

  // ── Filters ────────────────────────────────────────────────────────────────
  protected toggleFilter(key: LuckyFilterKey): void {
    this.rerollPhrases();
    this.lucky.toggleFilter(key);
  }

  // ── Actions ────────────────────────────────────────────────────────────────
  protected openDetail(): void {
    const id = this.anime?.aniDbId;
    if (id) this.animeSelected.emit(id);
  }

  protected openPlexLink(event: Event): void {
    if (!this.plexUrl) return;
    event.preventDefault();
    this.plexDeepLink.openLink(this.plexUrl, this.title);
  }

  protected showHistory(item: AnimeDetailsResponse): void {
    this.lucky.showHistorical(item);
    this.rerollPhrases();
  }

  protected historyCover(item: AnimeDetailsResponse): string | undefined {
    return item.malAnimes?.[0]?.coverImageUrl;
  }

  protected historyTitle(item: AnimeDetailsResponse): string {
    return item.malAnimes?.[0]?.alternativeTitles?.en?.trim() || item.title;
  }

  protected mediaTypeLabel(type: string): string {
    const key = `animeCard.mediaType.${type}`;
    const t = this.translate.instant(key);
    return t !== key ? t : type.toUpperCase();
  }

  protected viewStateLabel(state: string): string {
    const key = `animeDetailPanel.malStatus.${state}`;
    const t = this.translate.instant(key);
    return t !== key ? t : state;
  }

  protected seriesStatusLabel(status: string): string {
    const key = `animeDetailPanel.seriesStatus.${status}`;
    const t = this.translate.instant(key);
    return t !== key ? t : status.replace(/_/g, ' ');
  }

  protected translateGenre(name: string): string {
    const key = `genres.${name}`;
    const t = this.translate.instant(key);
    return t !== key ? t : name;
  }

  // ── Lifecycle ──────────────────────────────────────────────────────────────
  ngOnInit(): void {
    this.previouslyFocused = (document.activeElement as HTMLElement) ?? null;
    this.previousBodyOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    this.langSub = this.translate.onLangChange.subscribe(() => this.rerollPhrases());
    this.rerollPhrases();

    this.lucky.roll('open');

    // Land keyboard focus on the dialog so the tab trap engages immediately.
    setTimeout(() => this.cardRef()?.nativeElement.focus(), 0);
  }

  ngOnDestroy(): void {
    this.lucky.dismiss();
    this.langSub?.unsubscribe();
    document.body.style.overflow = this.previousBodyOverflow;
    this.previouslyFocused?.focus?.();
  }

  protected close(): void {
    this.lucky.dismiss();
    this.closed.emit();
  }

  protected tryAgain(): void {
    this.rerollPhrases();
    this.lucky.roll('reroll');
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      if (this.detailPanelOpen()) return; // the detail panel owns Escape while open
      this.close();
      return;
    }

    // R = reroll (ignored while typing in a field)
    if ((event.key === 'r' || event.key === 'R') && !event.ctrlKey && !event.metaKey && !event.altKey) {
      const el = document.activeElement;
      const typing =
        !!el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || (el as HTMLElement).isContentEditable);
      if (!typing && !this.detailPanelOpen()) {
        event.preventDefault();
        this.tryAgain();
      }
      return;
    }

    if (event.key === 'Tab') this.trapFocus(event);
  }

  /** Minimal focus trap: keeps Tab cycling inside the card. */
  private trapFocus(event: KeyboardEvent): void {
    const card = this.cardRef()?.nativeElement;
    if (!card) return;
    const focusables = Array.from(
      card.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), input, select, textarea, [tabindex]:not([tabindex="-1"])',
      ),
    ).filter((el) => el.offsetParent !== null);
    if (focusables.length === 0) return;

    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    const active = document.activeElement as HTMLElement | null;

    if (!event.shiftKey && (active === last || active === card)) {
      event.preventDefault();
      first.focus();
    } else if (event.shiftKey && (active === first || active === card)) {
      event.preventDefault();
      last.focus();
    }
  }

  // ── Copy ───────────────────────────────────────────────────────────────────
  private rerollPhrases(): void {
    this.quip.set(this.pickPhrase('luckyPick.phrases.reroll'));
    this.searchingPhrase.set(this.pickPhrase('luckyPick.phrases.searching'));
  }

  /**
   * Picks one random entry from an i18n array (falls back to the raw string when the
   * key resolves to something that is not an array).
   */
  private pickPhrase(key: string): string {
    const value: unknown = this.translate.instant(key);
    const arr: string[] = Array.isArray(value) ? (value as string[]) : [String(value)];
    return arr[Math.floor(Math.random() * arr.length)];
  }
}

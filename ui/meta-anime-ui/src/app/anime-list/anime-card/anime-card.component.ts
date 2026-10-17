import {
  ChangeDetectionStrategy,
  Component,
  computed,
  HostListener,
  input,
  output,
  signal,
  inject,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { trigger, style, animate, transition } from '@angular/animations';
import { AnimeDetailsResponse, MalAnimeInfo, SeasonalAnimeItem, SonarrAnimeInfo } from '../../models/anime.models';
import { ScoreBadgeComponent } from './score-badge/score-badge.component';
import { GENRE_LIST, THEME_LIST, DEMOGRAPHIC_LIST } from '../../constants/genre-lists';
import { ProviderStateService } from '../../services/provider-state.service';
import { BotKunService } from '../../services/bot-kun.service';

@Component({
  selector: 'app-anime-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, ScoreBadgeComponent, DatePipe],
  templateUrl: './anime-card.component.html',
  styleUrl: './anime-card.component.scss',
  animations: [
    trigger('accordionExpand', [
      transition(':enter', [
        style({ height: '0', opacity: '0', overflow: 'hidden' }),
        animate('280ms cubic-bezier(0.4, 0, 0.2, 1)', style({ height: '*', opacity: '1' })),
      ]),
      transition(':leave', [
        style({ overflow: 'hidden' }),
        animate('200ms cubic-bezier(0.4, 0, 1, 1)', style({ height: '0', opacity: '0' })),
      ]),
    ]),
  ],
})
export class AnimeCardComponent {
  readonly anime = input.required<AnimeDetailsResponse>();
  /** Emitted when the user clicks the card body to open the detail panel. */
  readonly openDetails = output<string>();
  /** When true, the first-time onboarding hints are shown on this card. */
  readonly showOnboardingHint = input(false);
  /** Emitted when the user dismisses the onboarding hint by interacting with the card. */
  readonly onboardingDismissed = output<void>();

  // ── Seasonal mode inputs ──────────────────────────────────────────────────
  readonly seasonalItem = input<SeasonalAnimeItem | null>(null);

  // ── Seasonal mode output ──────────────────────────────────────────────────
  readonly openDetailsByMalId = output<string>();

  /** When true, a brief blue glow animation plays on the card (successful add). */
  readonly glowSuccess = input<boolean>(false);

  private translate = inject(TranslateService);
  private providerState = inject(ProviderStateService);
  private botKunService = inject(BotKunService);

  readonly botkunAvailable = computed(() => this.providerState.botkunEnabled() && this.providerState.botkunConfigured());

  showAltTitles = signal(false);
  showLightbox = signal(false);
  activeProviderTooltip = signal<string | null>(null);

  // ── Lifecycle ─────────────────────────────────────────────────────────────

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.showLightbox()) {
      this.showLightbox.set(false);
    }
  }

  // ── Data accessors ────────────────────────────────────────────────────────

  get mal(): MalAnimeInfo | undefined {
    return this.anime().malAnimes?.[0];
  }

  get sonarr(): SonarrAnimeInfo | undefined {
    return this.anime().sonarrAnimes?.[0];
  }

  get plex() {
    return this.anime().plexAnimes?.[0];
  }

  // ── Seasonal data helpers (prefer seasonalItem when set) ──────────────────

  get malId(): string | null {
    return this.seasonalItem()?.malId ?? this.mal?.id ?? null;
  }
  // ── Title / image ─────────────────────────────────────────────────────────

  get coverImageUrl(): string | undefined {
    return this.seasonalItem()?.coverImageUrl ?? this.mal?.coverImageUrl;
  }

  /** Best-effort localised title based on current UI language */
  get displayTitle(): string {
    const seasonal = this.seasonalItem();
    if (seasonal) return seasonal.title;
    const lang = this.translate.getCurrentLang() ?? 'en';
    const alt = this.mal?.alternativeTitles;
    if (alt) {
      if (lang === 'ja' && alt.ja) return alt.ja;
      if (alt.en && alt.en !== this.anime().title) return alt.en;
    }
    return this.anime().title;
  }

  get subtitle(): string | undefined {
    const dt = this.displayTitle;
    if (dt !== this.anime().title) return this.anime().title;
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

  // ── Description (CSS-truncated to 3 lines — no "see more" modal) ──────────

  get description(): string {
    return this.seasonalItem()?.synopsis ?? this.mal?.synopsis ?? this.anime().description ?? '';
  }

  // ── Status ────────────────────────────────────────────────────────────────

  get malViewState(): string | undefined {
    return this.mal?.viewState;
  }

  get seriesStatus(): string | undefined {
    return this.seasonalItem()?.status ?? this.mal?.status;
  }

  get mediaType(): string | undefined {
    return this.seasonalItem()?.mediaType ?? this.mal?.mediaType;
  }

  get ageRating(): string | undefined {
    return this.seasonalItem()?.ageRating ?? this.mal?.ageRating;
  }

  mediaTypeLabel(type: string): string {
    const key = `animeDetailPanel.mediaType.${type.toLowerCase()}`;
    const t = this.translate.instant(key);
    return t !== key ? t : type.toUpperCase();
  }

  ageRatingLabel(rating: string): string {
    const key = `animeDetailPanel.ageRating.${rating}`;
    const t = this.translate.instant(key);
    return t !== key ? t : rating.toUpperCase();
  }

  viewStateLabel(state: string): string {
    const key = `animeDetailPanel.malStatus.${state.toLowerCase()}`;
    const t = this.translate.instant(key);
    return t !== key ? t : state;
  }

  seriesStatusLabel(status: string): string {
    const key = `animeDetailPanel.seriesStatus.${status.toLowerCase()}`;
    const t = this.translate.instant(key);
    return t !== key ? t : status.replace(/_/g, ' ');
  }

  // ── Genres ────────────────────────────────────────────────────────────────

  private get allMalGenres(): string[] {
    return this.seasonalItem()?.genres ?? this.mal?.genres ?? [];
  }

  get pureGenres(): string[] {
    return this.allMalGenres.filter((g) => GENRE_LIST.includes(g));
  }

  get themes(): string[] {
    return this.allMalGenres.filter((g) => THEME_LIST.includes(g));
  }

  get demographics(): string[] {
    return this.allMalGenres.filter((g) => DEMOGRAPHIC_LIST.includes(g));
  }

  translateGenre(name: string): string {
    const key = `genres.${name}`;
    const t = this.translate.instant(key);
    return t !== key ? t : name;
  }

  // ── Provider membership flags (drive ribbon indicators) ─────────────────

  get inMal(): boolean {
    const s = this.seasonalItem();
    return s !== null ? s.inMal : !!(this.mal?.id);
  }

  get inSonarr(): boolean {
    const s = this.seasonalItem();
    return s !== null ? s.inSonarr : !!(this.sonarr);
  }

  get inPlex(): boolean {
    const s = this.seasonalItem();
    return s !== null ? s.inPlex : !!(this.plex);
  }

  // ── Provider URLs ─────────────────────────────────────────────────────────

  get malUrl(): string | undefined {
    return this.seasonalItem()?.malUrl ?? this.mal?.url;
  }

  get sonarrUrl(): string | undefined {
    return this.seasonalItem()?.sonarrUrl ?? this.sonarr?.url;
  }

  get plexUrl(): string | undefined {
    return this.seasonalItem()?.plexUrl ?? this.plex?.url;
  }

  // ── Scores ────────────────────────────────────────────────────────────────

  get malScore(): number {
    return this.seasonalItem()?.malScore ?? this.mal?.rating?.malMean ?? 0;
  }

  get myScore(): number {
    return this.mal?.rating?.myScore ?? 0;
  }

  // ── Dates ─────────────────────────────────────────────────────────────────

  get startDate(): string | undefined {
    return this.seasonalItem()?.startDate ?? this.mal?.startDate;
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

  // ── Interaction ───────────────────────────────────────────────────────────

  toggleAltTitles(): void {
    this.showAltTitles.update((v) => !v);
  }

  /** Opens the detail panel. Dismisses onboarding hint if it was shown. */
  onContentClick(): void {
    if (this.showOnboardingHint()) this.onboardingDismissed.emit();
    const seasonal = this.seasonalItem();
    if (seasonal) {
      // Only route to the local /api/v1/animes/{id} endpoint when the show is
      // confirmed to be in the local library. Without this guard, shows that
      // have an aniDbId but are not yet tracked locally produce a 404.
      if (seasonal.aniDbId && (seasonal.inMal || seasonal.inSonarr || seasonal.inPlex)) {
        this.openDetails.emit(seasonal.aniDbId);
      } else {
        this.openDetailsByMalId.emit(seasonal.malId);
      }
      return;
    }
    this.openDetails.emit(this.anime().aniDbId);
  }

  openLightbox(): void {
    if (this.coverImageUrl) this.showLightbox.set(true);
  }

  closeLightbox(): void {
    this.showLightbox.set(false);
  }

  toggleTooltip(provider: string, event: Event): void {
    event.stopPropagation();
    this.activeProviderTooltip.update((current) => (current === provider ? null : provider));
  }

  askBotKun(event: Event): void {
    event.stopPropagation();

    const lines: string[] = [];

    const typePart = this.mediaType ? this.mediaTypeLabel(this.mediaType) : null;
    const statusPart = this.seriesStatus ? this.seriesStatusLabel(this.seriesStatus) : null;
    const typeStatus = [typePart, statusPart].filter(Boolean).join(' · ');

    lines.push(`Title: ${this.displayTitle}`);
    if (typeStatus) lines.push(`Type: ${typeStatus}`);

    const dateParts = [this.startDate, this.endDate].filter(Boolean);
    if (dateParts.length > 0) lines.push(`Release: ${dateParts.join(' → ')}`);

    const genres = [...this.pureGenres, ...this.themes];
    if (genres.length > 0) lines.push(`Genres: ${genres.join(', ')}`);

    if (this.malScore > 0) lines.push(`Score: ${this.malScore}/10`);
    if (this.ageRating) lines.push(`Age Rating: ${this.ageRatingLabel(this.ageRating)}`);

    if (this.description) lines.push(`\nSynopsis:\n${this.description}`);

    const instruction = this.translate.instant('animeCard.askBotKunInstruction');
    this.botKunService.requestPrompt(`${instruction}\n\n${lines.join('\n')}`);
  }
}

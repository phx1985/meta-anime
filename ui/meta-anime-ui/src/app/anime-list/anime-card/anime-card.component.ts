import { ChangeDetectionStrategy, Component, input, output, signal, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AnimeDetailsResponse, MalAnimeInfo, SonarrAnimeInfo } from '../../models/anime.models';
import { ScoreBadgeComponent } from './score-badge/score-badge.component';
import { GENRE_LIST, THEME_LIST, DEMOGRAPHIC_LIST } from '../../constants/genre-lists';

@Component({
  selector: 'app-anime-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, ScoreBadgeComponent, DatePipe],
  templateUrl: './anime-card.component.html',
  styleUrl: './anime-card.component.scss',
})
export class AnimeCardComponent {
  readonly anime = input.required<AnimeDetailsResponse>();
  /** Emitted when the user clicks the card body to open the detail panel. */
  readonly openDetails = output<string>();
  /** When true, the first-time onboarding hints are shown on this card. */
  readonly showOnboardingHint = input(false);
  /** Emitted when the user dismisses the onboarding hint by interacting with the card. */
  readonly onboardingDismissed = output<void>();

  private translate = inject(TranslateService);

  showAltTitles = signal(false);

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
    return this.mal?.synopsis ?? this.anime().description ?? '';
  }

  // ── Status ────────────────────────────────────────────────────────────────

  get malViewState(): string | undefined {
    return this.mal?.viewState;
  }

  get seriesStatus(): string | undefined {
    return this.mal?.status;
  }

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

  // ── Scores ────────────────────────────────────────────────────────────────

  get malScore(): number {
    return this.mal?.rating?.malMean ?? 0;
  }

  get myScore(): number {
    return this.mal?.rating?.myScore ?? 0;
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

  // ── Interaction ───────────────────────────────────────────────────────────

  toggleAltTitles(): void {
    this.showAltTitles.update((v) => !v);
  }

  /** Opens the detail panel. Dismisses onboarding hint if it was shown. */
  onContentClick(): void {
    if (this.showOnboardingHint()) this.onboardingDismissed.emit();
    this.openDetails.emit(this.anime().aniDbId);
  }
}

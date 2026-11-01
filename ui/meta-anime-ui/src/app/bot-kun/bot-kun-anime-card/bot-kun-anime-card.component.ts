import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { UpperCasePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../../services/anime.service';
import { UiBadgeComponent } from '../../shared/ui/ui-badge.component';

/** MAL airing-status keys — rendered translated via `animeDetailPanel.seriesStatus.*`. */
type AiringStatusKey = 'finished_airing' | 'currently_airing' | 'not_yet_aired';

@Component({
  selector: 'app-bot-kun-anime-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [UpperCasePipe, TranslatePipe, UiBadgeComponent],
  templateUrl: './bot-kun-anime-card.component.html',
  styleUrl: './bot-kun-anime-card.component.scss',
})
export class BotKunAnimeCardComponent implements OnInit {
  readonly malId = input.required<string>();
  readonly cardClicked = output<string>();

  protected readonly title      = signal<string>('');
  protected readonly coverUrl    = signal<string | null>(null);
  protected readonly loadState   = signal<'loading' | 'loaded' | 'error'>('loading');
  protected readonly year        = signal<string | null>(null);
  protected readonly airingStatus = signal<AiringStatusKey | null>(null);
  protected readonly genres      = signal<string[]>([]);
  protected readonly score       = signal<number | null>(null);
  protected readonly mediaType   = signal<string | null>(null);
  protected readonly ageRating   = signal<string | null>(null);

  /** Fixed star-row indices — a stable array so `@for` never re-diffs per CD pass. */
  protected readonly starIndices = [0, 1, 2, 3, 4] as const;
  /** First three genres — memoized so the template never allocates slices per CD. */
  protected readonly topGenres = computed(() => this.genres().slice(0, 3));
  protected readonly extraGenreCount = computed(() => Math.max(0, this.genres().length - 3));
  /** Precomputed star fills — one derivation per score change instead of per star per CD. */
  protected readonly starFills = computed(() => this.starIndices.map(i => this.starFill(i)));

  private readonly animeService = inject(AnimeService);
  private readonly destroyRef   = inject(DestroyRef);

  ngOnInit(): void {
    this.animeService
      .getAnimeByMalId(this.malId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: detail => {
          const mal = detail?.malAnimes?.[0];
          const url = mal?.coverImageUrl ?? null;
          this.coverUrl.set(url);
          this.loadState.set(url ? 'loaded' : 'error');

          this.title.set(mal?.alternativeTitles?.en || mal?.title || '');
          this.year.set(mal?.startDate?.slice(0, 4) ?? null);
          this.airingStatus.set(this.statusKey(mal?.status ?? null));
          this.genres.set(mal?.genres ?? []);
          this.score.set(mal?.rating?.malMean ?? null);
          this.mediaType.set(mal?.mediaType ?? null);
          this.ageRating.set(mal?.ageRating ?? null);
        },
        error: () => this.loadState.set('error'),
      });
  }

  onClick(): void {
    this.cardClicked.emit(this.malId());
  }

  /** Returns 'full', 'half', or 'empty' for star index 0-4 (5-star scale, MAL/2). */
  starFill(index: number): 'full' | 'half' | 'empty' {
    const s = this.score();
    if (s === null) return 'empty';
    const stars = s / 2; // 10-point → 5-star
    if (stars >= index + 1) return 'full';
    if (stars >= index + 0.5) return 'half';
    return 'empty';
  }

  /** Maps the raw MAL status to its translation key (null when unknown). */
  private statusKey(raw: string | null | undefined): AiringStatusKey | null {
    switch (raw?.toLowerCase()) {
      case 'finished_airing':   return 'finished_airing';
      case 'currently_airing':  return 'currently_airing';
      case 'not_yet_aired':     return 'not_yet_aired';
      default: return null;
    }
  }
}

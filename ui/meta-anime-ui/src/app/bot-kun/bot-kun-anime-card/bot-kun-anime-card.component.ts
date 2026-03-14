import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { UpperCasePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AnimeService } from '../../services/anime.service';

@Component({
  selector: 'app-bot-kun-anime-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [UpperCasePipe],
  templateUrl: './bot-kun-anime-card.component.html',
  styleUrl: './bot-kun-anime-card.component.scss',
})
export class BotKunAnimeCardComponent implements OnInit {
  readonly malId = input.required<string>();
  readonly title = input<string>('');

  readonly cardClicked = output<string>();

  protected readonly coverUrl    = signal<string | null>(null);
  protected readonly loadState   = signal<'loading' | 'loaded' | 'error'>('loading');
  protected readonly year        = signal<string | null>(null);
  protected readonly airingStatus = signal<string | null>(null);
  protected readonly genres      = signal<string[]>([]);
  protected readonly score       = signal<number | null>(null);
  protected readonly mediaType   = signal<string | null>(null);
  protected readonly ageRating   = signal<string | null>(null);

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

          this.year.set(mal?.startDate?.slice(0, 4) ?? null);
          this.airingStatus.set(this.formatStatus(mal?.status ?? null));
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

  private formatStatus(raw: string | null | undefined): string | null {
    switch (raw?.toLowerCase()) {
      case 'finished_airing':   return 'Finished';
      case 'currently_airing':  return 'Airing';
      case 'not_yet_aired':     return 'Upcoming';
      default: return null;
    }
  }
}

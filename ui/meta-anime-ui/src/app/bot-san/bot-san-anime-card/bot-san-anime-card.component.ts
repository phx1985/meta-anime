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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AnimeService } from '../../services/anime.service';

@Component({
  selector: 'app-bot-san-anime-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './bot-san-anime-card.component.html',
  styleUrl: './bot-san-anime-card.component.scss',
})
export class BotSanAnimeCardComponent implements OnInit {
  readonly malId = input.required<string>();
  readonly title = input.required<string>();

  readonly cardClicked = output<string>();

  protected readonly coverUrl = signal<string | null>(null);
  protected readonly loadState = signal<'loading' | 'loaded' | 'error'>('loading');

  private readonly animeService = inject(AnimeService);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    this.animeService
      .getAnimeByMalId(this.malId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: detail => {
          const url = detail?.malAnimes?.[0]?.coverImageUrl ?? null;
          this.coverUrl.set(url);
          this.loadState.set(url ? 'loaded' : 'error');
        },
        error: () => this.loadState.set('error'),
      });
  }

  onClick(): void {
    this.cardClicked.emit(this.malId());
  }
}

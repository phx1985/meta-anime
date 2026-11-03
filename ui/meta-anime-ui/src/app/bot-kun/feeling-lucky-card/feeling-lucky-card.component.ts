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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../../services/anime.service';
import { LuckyPickService } from '../../services/lucky-pick.service';
import { AnimeDetailsResponse } from '../../models/anime.models';
import { UiBadgeComponent } from '../../shared/ui/ui-badge.component';

type LuckyCardState = 'rolling' | 'loaded' | 'error';

@Component({
  selector: 'app-feeling-lucky-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, UiBadgeComponent],
  templateUrl: './feeling-lucky-card.component.html',
  styleUrl: './feeling-lucky-card.component.scss',
})
export class FeelingLuckyCardComponent implements OnInit {
  readonly malId = input.required<string>();
  readonly title = input<string>('');

  readonly cardReady = output<void>();
  readonly cardClicked = output<string>();

  protected readonly state          = signal<LuckyCardState>('rolling');
  protected readonly anime          = signal<AnimeDetailsResponse | null>(null);
  protected readonly coverUrl       = signal<string | null>(null);
  protected readonly displayTitle   = signal<string>('');
  protected readonly year           = signal<string | null>(null);
  protected readonly score          = signal<number | null>(null);
  protected readonly genres         = signal<string[]>([]);
  protected readonly mediaType      = signal<string | null>(null);
  protected readonly isAddingToList = signal(false);
  protected readonly addedToList    = signal(false);
  protected readonly actionErrorKey = signal<string | null>(null);
  /** Ids follow the displayed anime once the user rerolls inside the chat card. */
  protected readonly currentMalId = signal<string>('');

  protected readonly isRolling = computed(() => this.state() === 'rolling');
  protected readonly isLoaded  = computed(() => this.state() === 'loaded');
  protected readonly isError   = computed(() => this.state() === 'error');
  protected readonly isTrackedLocally = computed(() => {
    const anime = this.anime();
    if (!anime) return false;

    return anime.aniDbId !== this.malId()
      || (anime.sonarrAnimes?.length ?? 0) > 0
      || (anime.plexAnimes?.length ?? 0) > 0;
  });

  private readonly animeService = inject(AnimeService);
  private readonly lucky        = inject(LuckyPickService);
  private readonly destroyRef   = inject(DestroyRef);

  private rollTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.displayTitle.set(this.title());
    this.currentMalId.set(this.malId());

    // Clean up the timer if the component is destroyed before it fires
    this.destroyRef.onDestroy(() => {
      if (this.rollTimer !== null) clearTimeout(this.rollTimer);
    });

    // Load details in parallel with the rolling animation
    this.animeService
      .getAnimeByMalId(this.malId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: detail => this.applyAnime(detail, 1500),
        error: () => {
          // F10: clear any previous timer before scheduling a new one
          if (this.rollTimer !== null) clearTimeout(this.rollTimer);
          this.rollTimer = setTimeout(() => {
            this.state.set('error');
            this.cardReady.emit();
          }, 1500);
        },
      });
  }

  /** Reroll straight from the chat card — same engine as the modal. */
  onReroll(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.isRolling()) return;

    this.addedToList.set(false);
    this.actionErrorKey.set(null);
    this.state.set('rolling');

    this.lucky
      .pickOnce()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => (detail ? this.applyAnime(detail, 900) : this.state.set('error')),
        error: () => this.state.set('error'),
      });
  }

  private applyAnime(detail: AnimeDetailsResponse | null, spinMs: number): void {
    this.anime.set(detail);
    const mal = detail?.malAnimes?.[0];
    this.coverUrl.set(mal?.coverImageUrl ?? null);
    const en = mal?.alternativeTitles?.en;
    this.displayTitle.set(en ?? detail?.title ?? this.title());
    this.year.set(mal?.startDate?.slice(0, 4) ?? null);
    this.score.set(mal?.rating?.malMean ?? null);
    this.genres.set((mal?.genres ?? []).slice(0, 4));
    this.mediaType.set(mal?.mediaType ?? null);
    if (mal?.id) this.currentMalId.set(mal.id);

    // Always cancel a queued reveal before scheduling the next one
    if (this.rollTimer !== null) clearTimeout(this.rollTimer);
    this.rollTimer = setTimeout(() => {
      this.state.set('loaded');
      this.cardReady.emit();
    }, spinMs);
  }

  onOpenDetails(event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    this.cardClicked.emit(this.currentMalId());
  }

  onAddToList(event: Event): void {
    event.preventDefault();
    event.stopPropagation();

    if (this.isAddingToList() || this.addedToList()) return;

    this.isAddingToList.set(true);
    this.actionErrorKey.set(null);

    this.animeService
      .updateMalListStatus(this.currentMalId(), {
        status: 'plan_to_watch',
        numWatchedEpisodes: 0,
        score: 0,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isAddingToList.set(false);
          this.addedToList.set(true);

          const anime = this.anime();
          const currentMal = anime?.malAnimes?.[0];
          if (!anime || !currentMal) return;

          this.anime.set({
            ...anime,
            malAnimes: [{
              ...currentMal,
              viewState: 'plan_to_watch',
            }],
          });
        },
        error: () => {
          this.isAddingToList.set(false);
          this.actionErrorKey.set('luckyPick.chatCard.updateError');
        },
      });
  }
}

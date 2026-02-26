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
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { UserPreferencesService } from '../services/user-preferences.service';
import { AnimeDetailsResponse, AnimeFilters } from '../models/anime.models';

type LuckyState = 'idle' | 'spinning' | 'result' | 'no-match' | 'error';

// ── Humorous copy banks ──────────────────────────────────────────────────────
const PLEX_PHRASES = [
  'Summon the popcorn!',
  'Let the binge begin!',
  'Stream it before your resolve crumbles.',
  'Your sofa is calling.',
  'Skip sleep. Start streaming.',
  'Deploy the watching station!',
  'One does not simply watch one episode.',
  'Challenge accepted — marathon mode: ON.',
  'Lights, snacks, existential dread. Go!',
  'Your screen awaits, weeb.',
] as const;

const MAL_PHRASES = [
  'Add to the endless backlog.',
  'Register this into the void.',
  'Feed the algorithm.',
  'Pad that Plan-to-Watch list.',
  'Witness the MAL score drama.',
  'Judge it with numbers.',
  'Let strangers rate your taste.',
  '10/10 it before watching.',
  'Confirm it is, indeed, an anime.',
  'Log it for future abandonment.',
] as const;

const REROLL_PHRASES = [
  'Trash tier, roll again!',
  'Nope. Next!',
  'The prophecy demands a reroll.',
  'This one... is not it.',
  'Call upon the dice spirits!',
  'Fortune spins twice for cowards.',
  'One more spin of destiny.',
  'Rejected. Spin the wheel!',
  '404: hype not found. Retry.',
  'The RNG gods demand sacrifice.',
] as const;

@Component({
  selector: 'app-lucky-pick',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, RouterLink],
  templateUrl: './lucky-pick.component.html',
  styleUrl: './lucky-pick.component.scss',
})
export class LuckyPickComponent implements OnInit, OnDestroy {
  @Output() closed = new EventEmitter<void>();

  private animeService = inject(AnimeService);
  protected userPrefs = inject(UserPreferencesService);

  protected state = signal<LuckyState>('idle');
  protected result = signal<AnimeDetailsResponse | null>(null);

  /** Randomly-selected humorous button phrases, rerolled on every spin */
  protected plexPhrase   = signal(LuckyPickComponent.pickRandom(PLEX_PHRASES));
  protected malPhrase    = signal(LuckyPickComponent.pickRandom(MAL_PHRASES));
  protected rerollPhrase = signal(LuckyPickComponent.pickRandom(REROLL_PHRASES));

  /** True if the OS prefers reduced motion */
  protected readonly prefersReducedMotion = this.checkReducedMotion();

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

  protected get malUrl(): string | null {
    return this.anime?.malAnimes?.[0]?.url ?? null;
  }

  protected get plexUrl(): string | null {
    return this.anime?.plexAnimes?.[0]?.url ?? null;
  }

  // ── Lifecycle ─────────────────────────────────────────────────────────────

  ngOnInit(): void {
    this.spin();
  }

  // ── Spin trigger ─────────────────────────────────────────────────────────

  spin(): void {
    if (this.state() === 'spinning') return;
    this.result.set(null);
    this.state.set('spinning');

    // Reroll funny phrases on every spin so the copy feels fresh
    this.plexPhrase.set(LuckyPickComponent.pickRandom(PLEX_PHRASES));
    this.malPhrase.set(LuckyPickComponent.pickRandom(MAL_PHRASES));
    this.rerollPhrase.set(LuckyPickComponent.pickRandom(REROLL_PHRASES));

    const filters: AnimeFilters = {};

    if (this.userPrefs.luckyOnlyPlanToWatch()) {
      filters.status = ['plan_to_watch'];
    }

    if (this.userPrefs.luckyOnlyFinished()) {
      filters.airingStatus = ['finished_airing'];
    }

    if (this.userPrefs.luckyOnlyWithFiles()) {
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
  }

  private clearTimer(): void {
    if (this.spinTimer) {
      clearTimeout(this.spinTimer);
      this.spinTimer = null;
    }
  }

  /** Picks a uniformly-random element from a readonly array. */
  private static pickRandom<T>(arr: readonly T[]): T {
    return arr[Math.floor(Math.random() * arr.length)];
  }
}

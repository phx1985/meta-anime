import {
  Component,
  DestroyRef,
  inject,
  signal,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { SettingsService } from '../services/settings.service';
import { SeasonalAnimeItem, SeasonName } from '../models/anime.models';
import { SeasonalCardComponent } from './seasonal-card/seasonal-card.component';

const MIN_YEAR = 1963;

export interface SeasonPill {
  season: SeasonName;
  labelKey: string;
}

const ALL_SEASONS: SeasonPill[] = [
  { season: 'winter', labelKey: 'seasonal.season.winter' },
  { season: 'spring', labelKey: 'seasonal.season.spring' },
  { season: 'summer', labelKey: 'seasonal.season.summer' },
  { season: 'fall',   labelKey: 'seasonal.season.fall'   },
];

/** Returns the current season name based on the month (1–12). */
function getCurrentSeason(month: number): SeasonName {
  if (month <= 3) return 'winter';
  if (month <= 6) return 'spring';
  if (month <= 9) return 'summer';
  return 'fall';
}

@Component({
  selector: 'app-seasonal',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe, SeasonalCardComponent],
  templateUrl: './seasonal.component.html',
  styleUrl: './seasonal.component.scss',
})
export class SeasonalComponent {
  private animeService    = inject(AnimeService);
  private settingsService = inject(SettingsService);
  private destroyRef      = inject(DestroyRef);

  private readonly _now        = new Date();
  private readonly _maxYear    = this._now.getFullYear();
  private readonly _initSeason = getCurrentSeason(this._now.getMonth() + 1);

  // Year picker
  readonly allSeasons = ALL_SEASONS;
  readonly minYear    = MIN_YEAR;
  readonly maxYear    = this._maxYear;

  /** Array of years from minYear to maxYear (reversed: newest first). Used for <select>. */
  readonly yearOptions = Array.from(
    { length: this._maxYear - MIN_YEAR + 1 },
    (_, i) => this._maxYear - i
  );

  selectedYear   = signal(this._maxYear);
  selectedSeason = signal<SeasonName>(this._initSeason);

  readonly canGoPrev = computed(() => this.selectedYear() > MIN_YEAR);
  readonly canGoNext = computed(() => this.selectedYear() < this._maxYear);

  animes  = signal<SeasonalAnimeItem[]>([]);
  loading = signal(false);
  error   = signal(false);

  constructor() {
    // Fetch NSFW setting (not used locally but kept for future)
    this.settingsService.getShowNsfw()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();

    // Initial load
    this._loadCurrent();
  }

  // ── Year navigation ──────────────────────────────────────────────────────
  prevYear(): void {
    if (!this.canGoPrev()) return;
    this.selectedYear.update(y => y - 1);
    this.selectedSeason.set('winter');
    this._loadCurrent();
  }

  nextYear(): void {
    if (!this.canGoNext()) return;
    this.selectedYear.update(y => y + 1);
    this.selectedSeason.set('winter');
    this._loadCurrent();
  }

  setYear(year: number): void {
    const clamped = Math.max(MIN_YEAR, Math.min(this._maxYear, year));
    if (clamped === this.selectedYear()) return;
    this.selectedYear.set(clamped);
    this.selectedSeason.set('winter');
    this._loadCurrent();
  }

  // ── Season selection ─────────────────────────────────────────────────────
  selectSeason(season: SeasonName): void {
    if (this.selectedSeason() === season) return;
    this.selectedSeason.set(season);
    this._loadCurrent();
  }

  // ── Loading ──────────────────────────────────────────────────────────────
  private _loadCurrent(): void {
    const year   = this.selectedYear();
    const season = this.selectedSeason();

    this.animes.set([]);
    this.loading.set(true);
    this.error.set(false);

    this.animeService.getSeasonalAnimes(year, season)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => {
          this.animes.set(data);
          this.loading.set(false);
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
        }
      });
  }
}


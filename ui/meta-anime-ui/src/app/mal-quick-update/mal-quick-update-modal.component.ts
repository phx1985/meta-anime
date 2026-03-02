import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { PopupService } from '../services/popup.service';
import { AnimeService } from '../services/anime.service';
import { NotificationService } from '../services/notification.service';

export const MAL_STATUSES = [
  'watching',
  'completed',
  'on_hold',
  'dropped',
  'plan_to_watch',
] as const;

export type MalStatus = (typeof MAL_STATUSES)[number];

@Component({
  selector: 'app-mal-quick-update-modal',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './mal-quick-update-modal.component.html',
  styleUrl: './mal-quick-update-modal.component.scss',
})
export class MalQuickUpdateModalComponent implements OnInit {
  protected popup = inject(PopupService);
  private animeService = inject(AnimeService);
  private notifService = inject(NotificationService);

  protected readonly statuses = MAL_STATUSES;
  protected readonly scores = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10] as const;

  protected selectedStatus = signal<string>('');
  protected episodesWatched = signal<number>(0);
  protected selectedScore = signal<number>(0);
  protected saving = signal(false);

  protected ctx = computed(() => this.popup.malQuickUpdate());
  protected leaving = computed(() => this.popup.malQuickUpdateLeaving());

  ngOnInit(): void {
    const ctx = this.popup.malQuickUpdate();
    if (ctx) {
      this.selectedStatus.set(ctx.currentStatus ?? '');
      this.episodesWatched.set(ctx.currentEpisodes ?? 0);
      this.selectedScore.set(ctx.currentScore ?? 0);
    }
  }

  protected dismiss(): void {
    this.popup.dismissMalQuickUpdate();
  }

  protected selectStatus(status: string): void {
    this.selectedStatus.set(status);
  }

  protected selectScore(score: number): void {
    this.selectedScore.set(this.selectedScore() === score ? 0 : score);
  }

  protected incrementEpisodes(): void {
    const ctx = this.ctx();
    const max = ctx?.totalEpisodes ?? Infinity;
    this.episodesWatched.set(Math.min(this.episodesWatched() + 1, max));
  }

  protected decrementEpisodes(): void {
    this.episodesWatched.set(Math.max(0, this.episodesWatched() - 1));
  }

  protected onEpisodesInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    let val = parseInt(input.value, 10);
    if (isNaN(val) || val < 0) val = 0;
    const ctx = this.ctx();
    if (ctx?.totalEpisodes && val > ctx.totalEpisodes) val = ctx.totalEpisodes;
    this.episodesWatched.set(val);
    input.value = String(val);
  }

  protected save(): void {
    const ctx = this.ctx();
    if (!ctx?.malId || this.saving()) return;
    this.saving.set(true);
    this.animeService
      .updateMalListStatus(ctx.malId, {
        status: this.selectedStatus() || 'watching',
        numWatchedEpisodes: this.episodesWatched(),
        score: this.selectedScore(),
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.notifService.push({
            id: `mal-update-${Date.now()}`,
            severity: 'success',
            titleKey: 'malQuickUpdate.saveSuccess',
            messageKey: 'malQuickUpdate.saveSuccessMsg',
          });
          this.dismiss();
        },
        error: () => {
          this.saving.set(false);
          this.notifService.push({
            id: `mal-update-err-${Date.now()}`,
            severity: 'error',
            titleKey: 'malQuickUpdate.saveError',
            messageKey: 'malQuickUpdate.saveErrorMsg',
          });
        },
      });
  }
}

import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  HostListener,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { ConfirmTarget, CoverageGapItem } from '../models/anime.models';
import { AnimeDetailPanelComponent } from '../anime-detail-panel/anime-detail-panel.component';
import { AnimeService } from '../services/anime.service';

@Component({
  selector: 'app-gap-confirm-modal',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, AnimeDetailPanelComponent],
  templateUrl: './gap-confirm-modal.component.html',
  styleUrl: './gap-confirm-modal.component.scss',
})
export class GapConfirmModalComponent {
  private readonly animeService = inject(AnimeService);
  private readonly destroyRef = inject(DestroyRef);

  /** The local gap item (left side of the comparison) */
  sourceItem = input.required<CoverageGapItem>();

  /** The chosen MAL target (right side of the comparison) */
  targetItem = input.required<ConfirmTarget>();

  /** Gap type from parent — drives the breadcrumb icon and source label */
  gapType = input<string>('');

  /** Emitted when user dismisses the modal without adding */
  readonly cancelled = output<void>();

  /** Emitted after the MAL API successfully adds the entry; carries the MAL ID */
  readonly malAdded = output<string>();

  /** MAL ID for the detail panel overlay — null means closed. */
  readonly previewMalId = signal<string | null>(null);

  /** True while the MAL API call is in flight */
  readonly adding = signal(false);

  /** Non-null when the MAL API call returned an error */
  readonly addError = signal<string | null>(null);

  /** Dynamic label — driven by gapType for accuracy */
  readonly sourceLabel = computed(() => {
    switch (this.gapType()) {
      case 'sonarrNotInMal': return 'SONARR ITEM';
      case 'plexNotInMal':   return 'PLEX ITEM';
      default:
        if (this.sourceItem().sonarrUrl) return 'SONARR ITEM';
        if (this.sourceItem().plexUrl)   return 'PLEX ITEM';
        return 'LOCAL ITEM';
    }
  });

  /** The MAL URL for the external link inside the detail panel */
  readonly malUrl = computed(() => `https://myanimelist.net/anime/${this.targetItem().id}`);

  addToMal(): void {
    if (this.adding()) return;
    this.adding.set(true);
    this.addError.set(null);

    this.animeService
      .updateMalListStatus(this.targetItem().id, {
        status: 'plan_to_watch',
        numWatchedEpisodes: 0,
        score: 0,
      }, this.sourceItem().aniDbId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.adding.set(false);
          this.malAdded.emit(this.targetItem().id);
        },
        error: () => {
          this.adding.set(false);
          this.addError.set('home.missing.modal.addMalError');
        },
      });
  }

  openPreview(): void {
    this.previewMalId.set(this.targetItem().id);
  }

  closePreview(): void {
    this.previewMalId.set(null);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.previewMalId()) {
      this.closePreview();
    } else {
      this.cancelled.emit();
    }
  }

  onBackdropClick(): void {
    this.cancelled.emit();
  }

  onPanelClick(event: MouseEvent): void {
    event.stopPropagation();
  }
}

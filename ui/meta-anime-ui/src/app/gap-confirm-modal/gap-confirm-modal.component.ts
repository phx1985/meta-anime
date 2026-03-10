import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  computed,
  input,
  output,
  signal,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { ConfirmTarget, CoverageGapItem } from '../models/anime.models';
import { AnimeDetailPanelComponent } from '../anime-detail-panel/anime-detail-panel.component';

@Component({
  selector: 'app-gap-confirm-modal',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, AnimeDetailPanelComponent],
  templateUrl: './gap-confirm-modal.component.html',
  styleUrl: './gap-confirm-modal.component.scss',
})
export class GapConfirmModalComponent {
  /** The local gap item (left side of the comparison) */
  sourceItem = input.required<CoverageGapItem>();

  /** The chosen MAL target (right side of the comparison) */
  targetItem = input.required<ConfirmTarget>();

  /** Emitted when user dismisses the modal */
  readonly cancelled = output<void>();

  /** MAL ID for the detail panel overlay — null means closed. */
  readonly previewMalId = signal<string | null>(null);

  /** Dynamic label based on the source of the local item */
  readonly sourceLabel = computed(() => {
    if (this.sourceItem().plexUrl) return 'PLEX ITEM';
    if (this.sourceItem().sonarrUrl) return 'SONARR ITEM';
    return 'LOCAL ITEM';
  });

  /** The MAL URL for the "Open in MAL" link */
  readonly malUrl = computed(() => `https://myanimelist.net/anime/${this.targetItem().id}`);

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

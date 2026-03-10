import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  HostListener,
  inject,
  input,
  OnInit,
  output,
  signal,
  computed,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import {
  ConfirmTarget,
  CoverageGapItem,
} from '../models/anime.models';
import { GapSearchModalComponent } from '../gap-search-modal/gap-search-modal.component';
import { GapConfirmModalComponent } from '../gap-confirm-modal/gap-confirm-modal.component';

@Component({
  selector: 'app-gap-detail-modal',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, GapSearchModalComponent, GapConfirmModalComponent],
  templateUrl: './gap-detail-modal.component.html',
  styleUrl: './gap-detail-modal.component.scss',
})
export class GapDetailModalComponent implements OnInit {
  private animeService = inject(AnimeService);
  private destroyRef = inject(DestroyRef);

  gapType = input.required<string>();
  gapLabel = input.required<string>();

  readonly closeModal = output<void>();
  /** Emitted when a gap item was successfully added to MAL; carries the gapType for counter decrement */
  readonly gapItemResolved = output<string>();

  readonly loading = signal(true);
  readonly items = signal<CoverageGapItem[]>([]);
  readonly error = signal(false);

  readonly searchModalItem  = signal<CoverageGapItem | null>(null);
  readonly confirmItem      = signal<CoverageGapItem | null>(null);
  readonly confirmTarget    = signal<ConfirmTarget | null>(null);

  readonly isMappable = computed(() => {
    const t = this.gapType().toLowerCase();
    return t === 'plexnotinmal' || t === 'sonarrnotinmal';
  });

  progressPercent(item: CoverageGapItem): number {
    const p = item.plexProgress;
    if (!p || p.totalEpisodes === 0) return 0;
    return Math.round((p.viewedEpisodes / p.totalEpisodes) * 100);
  }

  ngOnInit(): void {
    this.animeService
      .getCoverageGap(this.gapType())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.items.set(data);
          this.loading.set(false);
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
        },
      });
  }

  openSearchModal(item: CoverageGapItem): void {
    if (!this.isMappable()) return;
    this.searchModalItem.set(item);
  }

  onSearchModalClosed(): void {
    this.searchModalItem.set(null);
  }

  onResultSelected(item: CoverageGapItem, target: ConfirmTarget): void {
    this.confirmItem.set(item);
    this.confirmTarget.set(target);
  }

  onCancelled(): void {
    this.confirmItem.set(null);
    this.confirmTarget.set(null);
  }

  onMalAdded(): void {
    const resolved = this.confirmItem();
    if (resolved) {
      this.items.update((list) => list.filter((i) => i.aniDbId !== resolved.aniDbId));
    }
    this.onCancelled();
    this.searchModalItem.set(null);
    this.gapItemResolved.emit(this.gapType());
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.confirmItem()) {
      this.onCancelled();
    } else if (this.searchModalItem()) {
      this.searchModalItem.set(null);
    } else {
      this.closeModal.emit();
    }
  }

  onBackdropClick(): void {
    this.closeModal.emit();
  }

  onPanelClick(event: MouseEvent): void {
    event.stopPropagation();
  }
}

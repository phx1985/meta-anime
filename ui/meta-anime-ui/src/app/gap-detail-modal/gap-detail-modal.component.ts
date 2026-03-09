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
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import { CoverageGapItem, MalSearchResult } from '../models/anime.models';
import { AnimeDetailPanelComponent } from '../anime-detail-panel/anime-detail-panel.component';

@Component({
  selector: 'app-gap-detail-modal',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, FormsModule, AnimeDetailPanelComponent],
  templateUrl: './gap-detail-modal.component.html',
  styleUrl: './gap-detail-modal.component.scss',
})
export class GapDetailModalComponent implements OnInit {
  private animeService = inject(AnimeService);
  private destroyRef = inject(DestroyRef);

  gapType = input.required<string>();
  gapLabel = input.required<string>();

  readonly closeModal = output<void>();

  readonly loading = signal(true);
  readonly items = signal<CoverageGapItem[]>([]);
  readonly error = signal(false);

  // "Add to MAL" search state per row — keyed by aniDbId
  readonly activeSearchRowId = signal<string | null>(null);
  readonly searchQuery = signal('');
  readonly searchLoading = signal(false);
  readonly searchResults = signal<MalSearchResult[]>([]);
  readonly searchError = signal(false);

  // Rows currently being animated out (resolving → exit)
  readonly removingIds = signal<Set<string>>(new Set());

  // MAL detail panel preview — set to a MAL ID to show the full detail panel
  readonly previewMalId = signal<string | null>(null);

  /** True when the current gap type can have "Add to MAL" action */
  readonly showAddToMal = computed(() => {
    const t = this.gapType().toLowerCase();
    return t === 'plexnotinmal' || t === 'sonarrnotinmal';
  });

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

  /** Returns the CSS classes for a given row */
  rowClass(item: CoverageGapItem): string {
    const removing = this.removingIds();
    if (removing.has(item.aniDbId + '-exit')) return 'gap-row--exit';
    if (removing.has(item.aniDbId + '-resolving')) return 'gap-row--resolving';
    return '';
  }

  /** Toggle the inline search panel for a row — auto-populates title and triggers search */
  toggleSearch(item: CoverageGapItem): void {
    if (this.activeSearchRowId() === item.aniDbId) {
      this.activeSearchRowId.set(null);
      this.searchQuery.set('');
      this.searchResults.set([]);
    } else {
      this.activeSearchRowId.set(item.aniDbId);
      this.searchError.set(false);
      this.searchResults.set([]);
      // Auto-populate with originalTitle (Romaji/EN) if available, else fall back to
      // the localized title. Never include the year — keep query broad for MAL API.
      this.searchQuery.set(item.originalTitle?.trim() || item.title);
      this.doSearch();
    }
  }

  /** Execute MAL search */
  doSearch(): void {
    const q = this.searchQuery().trim();
    if (!q) return;
    this.searchLoading.set(true);
    this.searchError.set(false);
    this.animeService
      .searchMalAnime(q)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (results) => {
          this.searchResults.set(results);
          this.searchLoading.set(false);
        },
        error: () => {
          this.searchError.set(true);
          this.searchLoading.set(false);
        },
      });
  }

  /** Called when user picks a MAL result for a given gap item */
  selectMalResult(item: CoverageGapItem, malResult: MalSearchResult): void {
    const plexSourceId = this.extractPlexSourceId(item.plexUrl);
    this.animeService
      .createManualMapping({
        aniDbId: item.aniDbId,
        plexSourceId: plexSourceId ?? '',
        plexTitle: item.title,
        malId: malResult.id,
        malTitle: malResult.title,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.animateRowOut(item.aniDbId),
        error: () => { /* keep the row; user can retry */ },
      });
  }

  /** Animate a successfully-matched row out of the list */
  private animateRowOut(aniDbId: string): void {
    // 1. Close search panel
    this.activeSearchRowId.set(null);

    // 2. Apply green highlight
    const resolving = new Set(this.removingIds());
    resolving.add(aniDbId + '-resolving');
    this.removingIds.set(resolving);

    // 3. After 400ms start collapse animation
    setTimeout(() => {
      const exiting = new Set(this.removingIds());
      exiting.delete(aniDbId + '-resolving');
      exiting.add(aniDbId + '-exit');
      this.removingIds.set(exiting);

      // 4. After animation, remove from list
      setTimeout(() => {
        this.items.update(list => list.filter(i => i.aniDbId !== aniDbId));
        const done = new Set(this.removingIds());
        done.delete(aniDbId + '-exit');
        this.removingIds.set(done);
      }, 350);
    }, 400);
  }

  /** Extract Plex SourceId from a Plex URL (last path segment) */
  private extractPlexSourceId(plexUrl?: string): string | null {
    if (!plexUrl) return null;
    const parts = plexUrl.split('/');
    return parts[parts.length - 1] || null;
  }

  /** Progress percentage (0-100) for display */
  progressPercent(item: CoverageGapItem): number {
    const p = item.plexProgress;
    if (!p || p.totalEpisodes === 0) return 0;
    return Math.round((p.viewedEpisodes / p.totalEpisodes) * 100);
  }

  /** Open the Anime Detail Panel for a MAL result preview */
  openPreview(malId: string): void {
    this.previewMalId.set(malId);
  }

  /** Close the Anime Detail Panel preview */
  closePreview(): void {
    this.previewMalId.set(null);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.previewMalId()) {
      this.previewMalId.set(null);
    } else if (this.activeSearchRowId()) {
      this.activeSearchRowId.set(null);
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

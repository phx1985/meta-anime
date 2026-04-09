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
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeService } from '../services/anime.service';
import {
  ConfirmTarget,
  CoverageGapItem,
  MalSearchResult,
} from '../models/anime.models';

@Component({
  selector: 'app-gap-search-modal',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, FormsModule],
  templateUrl: './gap-search-modal.component.html',
  styleUrl: './gap-search-modal.component.scss',
})
export class GapSearchModalComponent implements OnInit {
  private readonly animeService = inject(AnimeService);
  private readonly destroyRef = inject(DestroyRef);

  /** The gap item being mapped — drives initial search query */
  sourceItem = input.required<CoverageGapItem>();

  /** Gap type from parent — drives the breadcrumb icon in the header */
  gapType = input<string>('');

  /** Emitted when user selects a MAL result */
  readonly resultSelected = output<ConfirmTarget>();

  /** Emitted when user dismisses without selecting */
  readonly closed = output<void>();

  readonly searchQuery   = signal('');
  readonly searchLoading = signal(false);
  readonly searchResults = signal<ConfirmTarget[]>([]);
  readonly searchError   = signal(false);

  ngOnInit(): void {
    const item = this.sourceItem();
    const raw = item.originalTitle?.trim() || item.title;
    // Strip common special chars that degrade MAL search quality (e.g. colons in
    // "Dragon Ball Super: The Galactic Patrol Prisoner" can confuse the API query).
    const sanitized = raw.replace(/[:\?!]/g, ' ').replace(/\s{2,}/g, ' ').trim();
    this.searchQuery.set(sanitized);
    this.doSearch();
  }

  doSearch(): void {
    const q = this.searchQuery().trim();
    if (!q) return;
    this.searchLoading.set(true);
    this.searchError.set(false);

    this.animeService
      .searchMalAnime(q)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (results: MalSearchResult[]) => {
          this.searchResults.set(
            results.map((r) => ({
              id: r.id,
              title: r.title,
              year: r.year,
              thumbnailUrl: r.thumbnailUrl,
              mediaType: r.mediaType,
            } satisfies ConfirmTarget))
          );
          this.searchLoading.set(false);
        },
        error: () => {
          this.searchError.set(true);
          this.searchLoading.set(false);
        },
      });
  }

  selectResult(result: ConfirmTarget): void {
    this.resultSelected.emit(result);
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closed.emit();
  }

  onBackdropClick(): void {
    this.closed.emit();
  }

  onPanelClick(event: MouseEvent): void {
    event.stopPropagation();
  }
}

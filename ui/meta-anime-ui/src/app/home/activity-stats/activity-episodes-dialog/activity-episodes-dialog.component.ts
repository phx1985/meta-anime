import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  afterNextRender,
  computed,
  DestroyRef,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ActivityEpisodeItem } from '../../../models/anime.models';
import { formatBinarySize } from '../../../utils/format.utils';

/**
 * ActivityEpisodesDialogComponent
 *
 * Drill-down behind the activity card's counters: the actual episodes imported during the
 * selected period (series | SxxExx | size). Purely presentational — the owner fetches the
 * list, so the period stays owned by `ActivityStatsComponent`.
 *
 * A11y: modal overlay (role=dialog, aria-modal) with focus moved in on open and restored to
 * the trigger on close, Escape to dismiss, backdrop click to dismiss. Renders as a bottom
 * sheet under 640px so it stays usable on mobile.
 */
@Component({
  selector: 'app-activity-episodes-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './activity-episodes-dialog.component.html',
  styleUrl: './activity-episodes-dialog.component.scss',
})
export class ActivityEpisodesDialogComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);

  /** Episodes from the API (newest first, already capped by the server). */
  readonly items = input<ActivityEpisodeItem[]>([]);
  /** True number of episodes in the period — may exceed `items.length`. */
  readonly totalCount = input<number>(0);
  /** Total bytes for the whole period (not just the rows shown). */
  readonly totalBytes = input<number>(0);
  /** Pre-translated period label is owned by the dialog (key is passed so it follows language changes). */
  readonly periodLabelKey = input<string>('');
  readonly loading = input<boolean>(false);
  readonly error = input<boolean>(false);

  readonly closed = output<void>();
  readonly retry = output<void>();

  /** Bumped on language change so the translate.instant() labels below re-render. */
  private readonly langVersion = signal(0);

  /** "1 episode" / "12 episodes" — picked explicitly so French never says "1 épisodes". */
  readonly countLabel = computed(() => {
    this.langVersion(); // re-compute on language change
    const count = this.totalCount();
    const key = count === 1
      ? 'home.activityStats.episodesDialog.countOne'
      : 'home.activityStats.episodesDialog.countMany';
    return this.translate.instant(key, { count });
  });

  readonly totalLabel = computed(() => {
    this.langVersion();
    return formatBinarySize(this.totalBytes(), (k: string) => this.translate.instant(k));
  });

  /** True when the server capped the list — footer explains the truncation. */
  readonly isTruncated = computed(() => this.items().length < this.totalCount());

  constructor() {
    // Labels built with translate.instant() must follow a live language switch.
    this.translate.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.langVersion.update(v => v + 1));

    // Remember what had focus (the stat tile) so it can be restored on close, and lock the
    // page behind the overlay so the background does not scroll under the mobile sheet.
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    this.destroyRef.onDestroy(() => {
      document.body.style.overflow = previousOverflow;
      previouslyFocused?.focus?.();
    });

    afterNextRender(() => {
      // Land focus on the close button so Escape/Tab behave and screen readers announce the dialog.
      this.host.nativeElement.querySelector<HTMLElement>('[data-autofocus]')?.focus();
    });
  }

  formatSize(bytes: number): string {
    return formatBinarySize(bytes, (k: string) => this.translate.instant(k));
  }

  /** Sonarr-style episode code: S01E03 (S00E05 for specials). */
  episodeCode(item: ActivityEpisodeItem): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `S${pad(item.seasonNumber)}E${pad(item.episodeNumber)}`;
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closed.emit();
  }

  onBackdropClick(): void {
    // Only a click on the backdrop itself (not a bubble from the panel) closes the dialog.
    this.closed.emit();
  }
}

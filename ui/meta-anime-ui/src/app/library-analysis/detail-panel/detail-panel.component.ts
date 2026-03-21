import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { DecimalPipe, SlicePipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LibrarySeriesRow } from '../../models/library-analysis.models';
import { formatTableSize } from '../../utils/format.utils';

@Component({
  selector: 'app-la-detail-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, SlicePipe, DecimalPipe],
  templateUrl: './detail-panel.component.html',
  styleUrl: './detail-panel.component.scss',
})
export class DetailPanelComponent {
  private readonly translateService = inject(TranslateService);

  // ── Inputs ──────────────────────────────────────────────────────────────────
  readonly row = input<LibrarySeriesRow | null>(null);
  readonly sonarrRemoteUrl = input<string>('');

  // ── Outputs ─────────────────────────────────────────────────────────────────
  readonly closed = output<void>();

  // ── State ───────────────────────────────────────────────────────────────────
  readonly closing = signal(false);
  readonly copySuccess = signal(false);

  // ── Keyboard: escape to close ───────────────────────────────────────────────
  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.row()) this.close();
  }

  // ── Touch: swipe-right to close ─────────────────────────────────────────────
  private touchStartX = 0;

  @HostListener('touchstart', ['$event'])
  onTouchStart(event: TouchEvent): void {
    this.touchStartX = event.touches[0].clientX;
  }

  @HostListener('touchend', ['$event'])
  onTouchEnd(event: TouchEvent): void {
    const deltaX = event.changedTouches[0].clientX - this.touchStartX;
    if (deltaX >= 60) this.close();
  }

  // ── Methods ─────────────────────────────────────────────────────────────────

  close(): void {
    if (this.closing()) return;
    this.closing.set(true);
    setTimeout(() => {
      this.closing.set(false);
      this.closed.emit();
    }, 200);
  }

  async copyPath(path: string): Promise<void> {
    await navigator.clipboard.writeText(path);
    this.copySuccess.set(true);
    setTimeout(() => this.copySuccess.set(false), 2000);
  }

  formatSize(bytes: number | undefined): string {
    return formatTableSize(bytes, key => this.translateService.instant(key));
  }

  buildSonarrUrl(title: string): string {
    const slug = title.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');
    return `${this.sonarrRemoteUrl()}/series/${slug}`;
  }
}

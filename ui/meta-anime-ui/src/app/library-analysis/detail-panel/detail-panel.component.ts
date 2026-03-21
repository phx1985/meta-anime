import {
  ChangeDetectionStrategy,
  Component,
  computed,
  HostListener,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LibrarySeriesRow } from '../../models/library-analysis.models';
import { formatTableSize } from '../../utils/format.utils';

@Component({
  selector: 'app-la-detail-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, DecimalPipe],
  templateUrl: './detail-panel.component.html',
  styleUrl: './detail-panel.component.scss',
})
export class DetailPanelComponent {
  private readonly translateService = inject(TranslateService);

  // ── Inputs ──────────────────────────────────────────────────────────────────
  readonly row = input<LibrarySeriesRow | null>(null);
  readonly sonarrRemoteUrl = input<string>('');
  readonly avgEpSizeThresholdBytes = input<number>(1200 * 1024 * 1024);

  // ── Outputs ─────────────────────────────────────────────────────────────────
  readonly closed = output<void>();
  readonly requestAnimeDetail = output<string>();

  // ── State ───────────────────────────────────────────────────────────────────
  readonly closing = signal(false);
  readonly copySuccess = signal(false);

  // ── Sort state ───────────────────────────────────────────────────────────────
  protected readonly sortCol = signal<string>('size');
  protected readonly sortDir = signal<'asc' | 'desc'>('desc');

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

  // ── Computed ─────────────────────────────────────────────────────────────────

  protected readonly sortedFiles = computed(() => {
    const files = this.row()?.episodeFiles ?? [];
    const col = this.sortCol();
    const dir = this.sortDir();
    return [...files].sort((a, b) => {
      let va: string | number, vb: string | number;
      switch (col) {
        case 'size':     va = a.sizeBytes;           vb = b.sizeBytes;           break;
        case 'video':    va = a.videoCodec ?? '';     vb = b.videoCodec ?? '';    break;
        case 'audio':    va = a.audioCodec ?? '';     vb = b.audioCodec ?? '';    break;
        case 'res':      va = a.resolution ?? '';     vb = b.resolution ?? '';    break;
        case 'depth':    va = a.videoBitDepth ?? 0;   vb = b.videoBitDepth ?? 0;  break;
        case 'channels': va = a.audioChannels ?? 0;   vb = b.audioChannels ?? 0;  break;
        default:         va = a.relativePath;         vb = b.relativePath;
      }
      const cmp = va < vb ? -1 : va > vb ? 1 : 0;
      return dir === 'asc' ? cmp : -cmp;
    });
  });

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

  protected sortBy(col: string): void {
    if (this.sortCol() === col) {
      this.sortDir.update(d => d === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortCol.set(col);
      this.sortDir.set(col === 'size' ? 'desc' : 'asc');
    }
  }

  protected getFilename(relativePath: string): string {
    return relativePath.split(/[/\\]/).pop() ?? relativePath;
  }
}

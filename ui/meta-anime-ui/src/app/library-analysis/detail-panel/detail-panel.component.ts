import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  HostListener,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LibraryEpisodeFileInfo, LibrarySeriesRow } from '../../models/library-analysis.models';
import {
  codecByteTotal,
  codecSizeShare,
  deriveSeriesStatus,
  estimateSeriesSavings,
} from '../../models/library-intel.models';
import { formatTableSize } from '../../utils/format.utils';
import {
  buildHandBrakeQueue,
  HandBrakeQueueFile,
  handBrakeQueueFileName,
  quoteCliArgs,
  seasonOf,
  serializeHandBrakeQueue,
} from '../../utils/user-path.utils';
import { UserPathService } from '../../services/user-path.service';

/** FILES table mini-tab selection. */
type FileFilter = 'all' | 'h264' | 'hevc';

/** One segment of the stacked dual-codec composition bar. */
interface CodecSegment {
  codec: string;
  bytes: number;
  pct: number;
  color: string;
}

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
  protected readonly userPath = inject(UserPathService);

  // ── Inputs ──────────────────────────────────────────────────────────────────
  readonly row = input<LibrarySeriesRow | null>(null);
  readonly sonarrRemoteUrl = input<string>('');
  readonly avgEpSizeThresholdBytes = input<number>(1200 * 1024 * 1024);
  readonly avgGibPerHourThreshold = input<number>(1.2);

  // ── Outputs ─────────────────────────────────────────────────────────────────
  readonly closed = output<void>();
  readonly requestAnimeDetail = output<string>();

  // ── State ───────────────────────────────────────────────────────────────────
  readonly closing = signal(false);
  /** Clipboard feedback ("Copied!" flash on the Copy Paths trigger). */
  readonly copySuccess = signal(false);

  // ── FILES table mini-tabs ────────────────────────────────────────────────────
  protected readonly fileFilter = signal<FileFilter>('all');

  // ── Multi-episode selection (Set of episodeFileId) ───────────────────────────
  protected readonly selectedIds = signal<Set<number>>(new Set());

  // ── Split-dropdown 'Copy Paths' menu ─────────────────────────────────────────
  protected readonly exportMenuOpen = signal(false);

  constructor() {
    // Reset the mini-tab and selection whenever a different series is shown
    effect(() => {
      this.row();
      this.fileFilter.set('all');
      this.selectedIds.set(new Set());
      this.exportMenuOpen.set(false);
    });
  }

  // ── Sort state ───────────────────────────────────────────────────────────────
  protected readonly sortCol = signal<string>('size');
  protected readonly sortDir = signal<'asc' | 'desc'>('desc');

  // ── Keyboard: escape to close ───────────────────────────────────────────────
  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.row()) this.close();
  }

  // ── Click outside the split button closes its dropdown ──────────────────────
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement | null;
    if (this.exportMenuOpen() && !target?.closest('.la-split')) {
      this.exportMenuOpen.set(false);
    }
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
        case 'duration': va = a.durationSeconds ?? 0; vb = b.durationSeconds ?? 0; break;
        case 'gibPerHour':
          va = a.durationSeconds > 0 ? (a.sizeBytes / 1_073_741_824) / (a.durationSeconds / 3600) : 0;
          vb = b.durationSeconds > 0 ? (b.sizeBytes / 1_073_741_824) / (b.durationSeconds / 3600) : 0;
          break;
        default:         va = a.relativePath;         vb = b.relativePath;
      }
      const cmp = va < vb ? -1 : va > vb ? 1 : 0;
      return dir === 'asc' ? cmp : -cmp;
    });
  });

  protected readonly hasDurationEstimates = computed(
    () => this.row()?.episodeFiles?.some(f => f.isDurationEstimated) ?? false
  );

  // ── Season-aware header status badge (same vocabulary as the table) ────────

  protected readonly statusBadgeClass = computed(() => {
    const r = this.row();
    if (!r) return '';
    switch (deriveSeriesStatus(r)) {
      case 'seriesEndedComplete': return 'la-status-badge la-status-badge--ended';
      case 'incompleteOnDisk':    return 'la-status-badge la-status-badge--incomplete';
      case 'seasonEndedHiatus':
      case 'airingWeekly':
      default:                    return 'la-status-badge la-status-badge--amber';
    }
  });

  protected readonly statusBadgeKey = computed(() => {
    const r = this.row();
    return r ? `libraryIntel.badge.${deriveSeriesStatus(r)}` : '';
  });

  // ── Stacked dual-codec composition ───────────────────────────────────────────

  protected readonly codecSegments = computed<CodecSegment[]>(() => {
    const r = this.row();
    if (!r) return [];
    const dist = r.videoCodecDistribution;
    const codecs = dist
      ? Object.keys(dist).filter(k => (dist[k] ?? 0) > 0)
      : r.primaryVideoCodec
        ? [r.primaryVideoCodec]
        : [];
    return codecs
      .map(codec => ({
        codec: this.normCodec(codec),
        bytes: codecByteTotal(r, codec),
        pct: codecSizeShare(r, codec),
        color: this.codecColor(codec),
      }))
      .sort((a, b) => b.bytes - a.bytes);
  });

  /** More than one distinct video codec → stacked bar replaces the single codec stat. */
  protected readonly hasMixedCodecs = computed(() => this.codecSegments().length > 1);

  // ── Est. savings ─────────────────────────────────────────────────────────────

  protected readonly estSavingsGb = computed(() => {
    const r = this.row();
    return r ? estimateSeriesSavings(r, this.userPath.includeUnknown()) / 1_073_741_824 : 0;
  });

  // ── Files filter tabs ────────────────────────────────────────────────────────

  protected readonly allFileCount = computed(() => this.row()?.episodeFiles?.length ?? 0);

  protected readonly h264Files = computed(
    () => (this.row()?.episodeFiles ?? []).filter(f => this.isH264(f.videoCodec))
  );

  protected readonly h264FileCount = computed(() => this.h264Files().length);

  protected readonly hevcFileCount = computed(
    () => (this.row()?.episodeFiles ?? []).filter(f => this.isHevc(f.videoCodec)).length
  );

  /** Sorted file list narrowed to the selected mini-tab. */
  protected readonly visibleFiles = computed(() => {
    const filter = this.fileFilter();
    const files = this.sortedFiles();
    if (filter === 'h264') return files.filter(f => this.isH264(f.videoCodec));
    if (filter === 'hevc') return files.filter(f => this.isHevc(f.videoCodec));
    return files;
  });

  // ── Multi-episode selection ──────────────────────────────────────────────────

  protected readonly selectedCount = computed(() => this.selectedIds().size);

  protected readonly selectedFiles = computed<LibraryEpisodeFileInfo[]>(() => {
    const ids = this.selectedIds();
    return (this.row()?.episodeFiles ?? []).filter(f => ids.has(f.episodeFileId));
  });

  /**
   * Export/copy scope: the checkbox selection, or every H264 episode when
   * nothing is selected (the natural 'needs transcode' worklist).
   */
  protected readonly actionFiles = computed<LibraryEpisodeFileInfo[]>(() => {
    const sel = this.selectedFiles();
    return sel.length ? sel : this.h264Files();
  });

  /** Resolved HOST paths of the export scope (row.path + relativePath → mapping). */
  protected readonly actionHostPaths = computed(() =>
    this.actionFiles().map(f => this.userPath.resolve(this.filePath(f)))
  );

  /** Number of episodes the HandBrake queue export will contain. */
  protected readonly exportQueueCount = computed(() => this.actionFiles().length);

  protected readonly allVisibleSelected = computed(() => {
    const visible = this.visibleFiles();
    const ids = this.selectedIds();
    return visible.length > 0 && visible.every(f => ids.has(f.episodeFileId));
  });

  protected readonly someVisibleSelected = computed(() => {
    const ids = this.selectedIds();
    return this.visibleFiles().some(f => ids.has(f.episodeFileId));
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

  // ── Path display (container → host) ─────────────────────────────────────────

  /** Displayed/copied paths always go through the user's host mapping. */
  protected resolvedPath(containerPath: string): string {
    return this.userPath.resolve(containerPath);
  }

  protected isPathMapped(containerPath: string): boolean {
    return this.userPath.isMapped(containerPath);
  }

  // ── Clipboard / exports ────────────────────────────────────────────────────

  private async copyText(text: string): Promise<void> {
    await navigator.clipboard.writeText(text);
    this.copySuccess.set(true);
    setTimeout(() => this.copySuccess.set(false), 2000);
  }

  /** Resolved host folder; appends the season subfolder when the scope is a single season. */
  protected async copyFolderPath(): Promise<void> {
    const r = this.row();
    if (!r) return;
    const scoped = this.selectedFiles().length ? this.selectedFiles() : this.visibleFiles();
    const seasons = new Set(scoped.map(f => seasonOf(f.relativePath)));
    let folder = r.path;
    if (seasons.size === 1) {
      const season = [...seasons][0];
      if (season) folder = `${r.path}/${season}`;
    }
    await this.copyText(this.userPath.resolve(folder));
    this.exportMenuOpen.set(false);
  }

  /** Dropdown item: newline-joined resolved host paths of the export scope. */
  protected async copyPlainPaths(): Promise<void> {
    const paths = this.actionHostPaths();
    if (!paths.length) return;
    await this.copyText(paths.join('\n'));
    this.exportMenuOpen.set(false);
  }

  /** Dropdown item: quoted CLI arguments, one per line. */
  protected async copyQuotedCliArgs(): Promise<void> {
    const paths = this.actionHostPaths();
    if (!paths.length) return;
    await this.copyText(quoteCliArgs(paths));
    this.exportMenuOpen.set(false);
  }

  /**
   * Primary CTA: downloads a native HandBrake queue.json over the export scope
   * (selected episodes, or all H264 episodes when nothing is selected).
   */
  protected exportQueue(): void {
    const r = this.row();
    const files = this.actionFiles();
    if (!r || !files.length) return;
    const queueFiles: HandBrakeQueueFile[] = files.map(f => ({
      hostSourcePath: this.userPath.resolve(this.filePath(f)),
      sourceFileName: this.fileNameOf(f),
      durationSeconds: f.durationSeconds ?? 0,
      resolution: f.resolution ?? '',
      videoCodec: f.videoCodec,
      bitDepth: f.videoBitDepth,
    }));
    const queue = buildHandBrakeQueue(
      queueFiles,
      this.userPath.destDir(),
      this.userPath.destMode(),
      this.userPath.presetName()
    );
    const blob = new Blob([serializeHandBrakeQueue(queue)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = handBrakeQueueFileName(r.title);
    link.click();
    URL.revokeObjectURL(url);
    this.exportMenuOpen.set(false);
  }

  // ── Selection toggles ──────────────────────────────────────────────────────

  protected isSelected(episodeFileId: number): boolean {
    return this.selectedIds().has(episodeFileId);
  }

  protected toggleFile(episodeFileId: number): void {
    this.selectedIds.update(ids => {
      const next = new Set(ids);
      if (next.has(episodeFileId)) next.delete(episodeFileId);
      else next.add(episodeFileId);
      return next;
    });
  }

  /** Master checkbox: selects/deselects every currently visible (filtered) file. */
  protected toggleAllVisible(): void {
    const visible = this.visibleFiles();
    const allSelected = this.allVisibleSelected();
    this.selectedIds.update(ids => {
      const next = new Set(ids);
      for (const f of visible) {
        if (allSelected) next.delete(f.episodeFileId);
        else next.add(f.episodeFileId);
      }
      return next;
    });
  }

  /** 'Select All Filtered ({{count}})' toggle button. */
  protected toggleSelectAllFiltered(): void {
    this.toggleAllVisible();
  }

  protected clearSelection(): void {
    this.selectedIds.set(new Set());
    this.exportMenuOpen.set(false);
  }

  /** Clicking the 'Needs Transcode / H264' tab auto-selects exactly those files. */
  protected selectH264Tab(): void {
    this.fileFilter.set('h264');
    this.selectedIds.set(new Set(this.h264Files().map(f => f.episodeFileId)));
  }

  formatSize(bytes: number | undefined): string {
    return formatTableSize(bytes, key => this.translateService.instant(key));
  }

  formatDuration(seconds: number): string {
    if (!seconds) return '—';
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = seconds % 60;
    const mm = String(m).padStart(2, '0');
    const ss = String(s).padStart(2, '0');
    return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
  }

  formatEpGibPerHour(sizeBytes: number, durationSeconds: number): string {
    if (!durationSeconds) return '—';
    const gph = (sizeBytes / 1_073_741_824) / (durationSeconds / 3600);
    return gph.toFixed(2);
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

  /** Full container path of a file: row.path + '/' + relativePath, or relativePath alone. */
  protected filePath(f: LibraryEpisodeFileInfo): string {
    const base = this.row()?.path ?? '';
    return base ? `${base}/${f.relativePath}` : f.relativePath;
  }

  /** Full HOST path of a file (hover tooltip + copied in exports). */
  protected resolvedFilePath(f: LibraryEpisodeFileInfo): string {
    return this.userPath.resolve(this.filePath(f));
  }

  /** File name with extension, derived from the relative path. */
  protected fileNameOf(f: LibraryEpisodeFileInfo): string {
    const parts = (f.relativePath ?? '').split(/[\\/]/);
    return parts[parts.length - 1] || f.relativePath || '';
  }

  /** Compact 'S01E01' badge derived from the relative path / file name. */
  protected episodeBadge(f: LibraryEpisodeFileInfo): string {
    const raw = f.relativePath ?? '';
    const m = raw.match(/S(\d+)[\s._-]*E(\d+)/i) ?? raw.match(/\b(\d{1,2})[xX](\d{2,3})\b/);
    if (!m) return '—';
    const season = String(Math.max(0, Number(m[1]))).padStart(2, '0');
    const episode = String(Math.max(0, Number(m[2]))).padStart(2, '0');
    return `S${season}E${episode}`;
  }

  protected normCodec(codec: string | undefined | null): string {
    return (codec ?? '').trim().toUpperCase();
  }

  protected isH264(codec: string | undefined | null): boolean {
    return this.normCodec(codec) === 'H264';
  }

  protected isHevc(codec: string | undefined | null): boolean {
    return this.normCodec(codec) === 'HEVC';
  }

  /** Segment colors: H264 amber, HEVC emerald, AV1 indigo, others slate. */
  protected codecColor(codec: string | undefined | null): string {
    switch (this.normCodec(codec)) {
      case 'H264': return '#f59e0b';
      case 'HEVC': return '#22c55e';
      case 'AV1':  return '#7c6cf0';
      default:     return '#64748b';
    }
  }
}

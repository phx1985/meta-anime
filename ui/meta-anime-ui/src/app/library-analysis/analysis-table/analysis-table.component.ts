import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
  WritableSignal,
} from '@angular/core';
import { LowerCasePipe } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { FormsModule } from '@angular/forms';
import { LibrarySeriesRow } from '../../models/library-analysis.models';
import { formatTableSize } from '../../utils/format.utils';

@Component({
  selector: 'app-la-analysis-table',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, LowerCasePipe, FormsModule],
  templateUrl: './analysis-table.component.html',
  styleUrl: './analysis-table.component.scss',
})
export class AnalysisTableComponent {
  private readonly translateService = inject(TranslateService);

  // ── Inputs ──────────────────────────────────────────────────────────────────
  readonly rows = input<LibrarySeriesRow[]>([]);
  readonly isEnriching = input<boolean>(false);

  // ── Outputs ─────────────────────────────────────────────────────────────────
  readonly rowClicked = output<LibrarySeriesRow>();

  // ── Sorting ─────────────────────────────────────────────────────────────────
  readonly sortCol = signal<string>('totalSizeBytes');
  readonly sortDir = signal<'asc' | 'desc'>('desc');

  // ── Filters ─────────────────────────────────────────────────────────────────
  readonly filterTitle = signal<string>('');
  readonly filterVideoCodec = signal<string[]>([]);
  readonly filterAudioCodec = signal<string[]>([]);
  readonly filterResolution = signal<string[]>([]);
  readonly filterAvgEpSize = signal<string>('');
  readonly filterStatus = signal<string>('');
  readonly filterDownload = signal<string>('');
  readonly filterBitDepth = signal<string>('');

  // ── Pagination ──────────────────────────────────────────────────────────────
  readonly page = signal<number>(1);
  readonly pageSize = signal<number>(25);
  readonly pageSizeOptions = [25, 50, 100] as const;

  // ── Filter constants ────────────────────────────────────────────────────────
  readonly VIDEO_CODECS = ['H264', 'HEVC', 'AV1', 'MPEG2'];
  readonly AUDIO_CODECS = ['AAC', 'AC3', 'FLAC', 'DTS', 'WAV', 'OPUS'];
  readonly RESOLUTIONS = ['4K', '1080p', '720p', '480p', 'SD'];
  readonly AVG_EP_SIZES = [
    { label: '> 500 MB', value: '>500mb', bytes: 500 * 1024 * 1024 },
    { label: '> 1 GB', value: '>1gb', bytes: 1024 * 1024 * 1024 },
    { label: '> 1.5 GB', value: '>1.5gb', bytes: 1.5 * 1024 * 1024 * 1024 },
  ];

  // ── Table columns ───────────────────────────────────────────────────────────
  readonly tableColumns = [
    { key: 'title', label: 'library.table.title', sortable: true },
    { key: 'primaryVideoCodec', label: 'library.table.videoCodec', sortable: true },
    { key: 'primaryAudioCodec', label: 'library.table.audioCodec', sortable: true },
    { key: 'primaryResolution', label: 'library.table.resolution', sortable: true },
    { key: 'fileCount', label: 'library.table.fileCount', sortable: true },
    { key: 'totalSizeBytes', label: 'library.table.totalSize', sortable: true },
    { key: 'avgEpisodeSizeBytes', label: 'library.table.avgEpSize', sortable: true },
    { key: 'primaryBitDepth', label: 'library.table.bitDepth', sortable: true },
  ];

  // ── Computed: filtered + sorted rows ────────────────────────────────────────
  readonly filteredRows = computed(() => {
    let result = [...this.rows()];

    // Title filter
    const title = this.filterTitle().toLowerCase().trim();
    if (title) {
      result = result.filter(r => r.title.toLowerCase().includes(title));
    }

    // Video codec filter
    const vCodecs = this.filterVideoCodec();
    if (vCodecs.length) {
      result = result.filter(r => r.primaryVideoCodec != null && vCodecs.includes(r.primaryVideoCodec));
    }

    // Audio codec filter
    const aCodecs = this.filterAudioCodec();
    if (aCodecs.length) {
      result = result.filter(r => r.primaryAudioCodec != null && aCodecs.includes(r.primaryAudioCodec));
    }

    // Resolution filter
    const res = this.filterResolution();
    if (res.length) {
      result = result.filter(r => r.primaryResolution != null && res.includes(r.primaryResolution));
    }

    // Avg episode size filter
    const avgSz = this.filterAvgEpSize();
    if (avgSz) {
      const threshold = this.AVG_EP_SIZES.find(s => s.value === avgSz)?.bytes ?? 0;
      result = result.filter(r => r.avgEpisodeSizeBytes > threshold);
    }

    // Status filter
    const status = this.filterStatus();
    if (status === 'ended') result = result.filter(r => r.ended);
    if (status === 'continuing') result = result.filter(r => !r.ended);

    // Download % filter
    const dl = this.filterDownload();
    if (dl === '100') result = result.filter(r => r.percentDownloaded >= 100);
    if (dl === 'partial') result = result.filter(r => r.percentDownloaded < 100 && r.percentDownloaded > 0);

    // Bit depth filter
    const bd = this.filterBitDepth();
    if (bd) result = result.filter(r => r.primaryBitDepth === +bd);

    // Sort
    const col = this.sortCol();
    const dir = this.sortDir();
    result.sort((a, b) => {
      let va: string | number | boolean | undefined = (a as unknown as Record<string, unknown>)[col] as string | number | boolean | undefined;
      let vb: string | number | boolean | undefined = (b as unknown as Record<string, unknown>)[col] as string | number | boolean | undefined;

      if (typeof va === 'string' && typeof vb === 'string') {
        va = va.toLowerCase();
        vb = vb.toLowerCase();
      }

      const fallback = dir === 'asc' ? Infinity : -Infinity;
      const sortA = va ?? fallback;
      const sortB = vb ?? fallback;

      return dir === 'asc'
        ? (sortA > sortB ? 1 : sortA < sortB ? -1 : 0)
        : (sortA < sortB ? 1 : sortA > sortB ? -1 : 0);
    });

    return result;
  });

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.filteredRows().length / this.pageSize())));

  readonly pagedRows = computed(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filteredRows().slice(start, start + this.pageSize());
  });

  readonly pageRange = computed<(number | '...')[]>(() => {
    const total = this.totalPages();
    const current = this.page();
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);

    const pages: (number | '...')[] = [1];
    if (current > 3) pages.push('...');
    for (let i = Math.max(2, current - 1); i <= Math.min(total - 1, current + 1); i++) {
      pages.push(i);
    }
    if (current < total - 2) pages.push('...');
    if (total > 1) pages.push(total);
    return pages;
  });

  readonly activeFilterCount = computed(() => {
    let count = 0;
    if (this.filterTitle().trim()) count++;
    if (this.filterVideoCodec().length) count++;
    if (this.filterAudioCodec().length) count++;
    if (this.filterResolution().length) count++;
    if (this.filterAvgEpSize()) count++;
    if (this.filterStatus()) count++;
    if (this.filterDownload()) count++;
    if (this.filterBitDepth()) count++;
    return count;
  });

  // ── Methods ─────────────────────────────────────────────────────────────────

  toggleSort(col: string): void {
    if (this.sortCol() === col) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortCol.set(col);
      this.sortDir.set('desc');
    }
    this.page.set(1);
  }

  toggleFilter(filterSignal: WritableSignal<string[]>, value: string): void {
    const current = filterSignal();
    if (current.includes(value)) {
      filterSignal.set(current.filter(v => v !== value));
    } else {
      filterSignal.set([...current, value]);
    }
    this.page.set(1);
  }

  clearFilters(): void {
    this.filterTitle.set('');
    this.filterVideoCodec.set([]);
    this.filterAudioCodec.set([]);
    this.filterResolution.set([]);
    this.filterAvgEpSize.set('');
    this.filterStatus.set('');
    this.filterDownload.set('');
    this.filterBitDepth.set('');
    this.page.set(1);
  }

  goToPage(p: number | '...'): void {
    if (p === '...') return;
    this.page.set(Math.max(1, Math.min(p, this.totalPages())));
  }

  formatSize(bytes: number | undefined): string {
    return formatTableSize(bytes, key => this.translateService.instant(key));
  }
}

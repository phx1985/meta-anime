import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  HostListener,
  inject,
  input,
  output,
  signal,
  ViewChild,
} from '@angular/core';
import { DecimalPipe, NgClass } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LibrarySeriesRow } from '../../models/library-analysis.models';
import {
  CodecMatchMode,
  CodecPercentOp,
  CodecThreshold,
  LibraryFilterState,
  LibraryStatusFilter,
  SeriesAirStatus,
  codecFileShare,
  deriveSeriesStatus,
  isSplitCodec,
  isTranscodeReady,
  matchesCodecThreshold,
} from '../../models/library-intel.models';
import { LibraryIntelStore } from '../../services/library-intel-store.service';
import { formatTableSize, getCodecPills } from '../../utils/format.utils';
import { UiSelectComponent, UiSelectOption } from '../../shared/ui/ui-select/ui-select.component';

@Component({
  selector: 'app-la-analysis-table',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, NgClass, DecimalPipe, UiSelectComponent],
  templateUrl: './analysis-table.component.html',
  styleUrl: './analysis-table.component.scss',
})
export class AnalysisTableComponent {
  private readonly translateService = inject(TranslateService);
  /** Shared Ultra Library Buddy filter state — single source of truth (KPI chips + Bot-kun write here too). */
  protected readonly store = inject(LibraryIntelStore);

  // ── Inputs ──────────────────────────────────────────────────────────────────
  readonly rows = input<LibrarySeriesRow[]>([]);
  readonly isEnriching = input<boolean>(false);
  readonly avgEpSizeThresholdBytes = input<number>(1200 * 1024 * 1024);
  readonly avgGibPerHourThreshold = input<number>(1.2);

  // ── Outputs ─────────────────────────────────────────────────────────────────
  readonly rowClicked = output<LibrarySeriesRow>();

  // ── Utility exposed to template ─────────────────────────────────────────────
  protected readonly getCodecPills = getCodecPills;

  // ── Shared filter state (LibraryIntelStore.filters) ─────────────────────────
  readonly filters = this.store.filters;

  // ── Pagination ──────────────────────────────────────────────────────────────
  readonly pageSizeOptions = [20, 25, 50, 100] as const;

  // ── Filter constants ────────────────────────────────────────────────────────
  readonly VIDEO_CODECS = ['H264', 'HEVC', 'AV1', 'MPEG2'];
  readonly AUDIO_CODECS = ['AAC', 'AC3', 'FLAC', 'DTS', 'WAV', 'OPUS'];
  readonly RESOLUTIONS = ['4K', '1080p', '720p', '480p', 'SD'];

  /** Numeric threshold presets — LibraryFilterState stores them as strings ('' = off). */
  readonly AVG_EP_SIZES = [
    { label: '> 500 MB', value: '500' },
    { label: '> 1 GB', value: '1024' },
    { label: '> 1.5 GB', value: '1536' },
  ];

  readonly GIB_PER_HOUR_THRESHOLDS = [
    { label: '> 0.5 GiB/h', value: '0.5' },
    { label: '> 1 GiB/h', value: '1' },
    { label: '> 2 GiB/h', value: '2' },
    { label: '> 4 GiB/h', value: '4' },
  ];

  /** Season-aware status filter options (LibraryStatusFilter). */
  readonly STATUS_OPTIONS: LibraryStatusFilter[] = [
    'all',
    'seriesEndedComplete',
    'seasonEndedHiatus',
    'airingWeekly',
    'incompleteOnDisk',
    'transcodeReady',
  ];

  readonly STATUS_LABELS: Record<LibraryStatusFilter, string> = {
    all: 'libraryIntel.status.all',
    seriesEndedComplete: 'libraryIntel.status.seriesEndedComplete',
    seasonEndedHiatus: 'libraryIntel.status.seasonEndedHiatus',
    airingWeekly: 'libraryIntel.status.airingWeekly',
    incompleteOnDisk: 'libraryIntel.status.incompleteOnDisk',
    transcodeReady: 'libraryIntel.status.transcodeReady',
  };

  /** Codec match modes (CodecThreshold.mode). */
  readonly MATCH_MODES: CodecMatchMode[] = ['any', 'strict', 'mixed', 'custom'];

  readonly MATCH_MODE_LABELS: Record<CodecMatchMode, string> = {
    any: 'libraryIntel.matchMode.any',
    strict: 'libraryIntel.matchMode.strict',
    mixed: 'libraryIntel.matchMode.mixed',
    custom: 'libraryIntel.matchMode.custom',
  };

  // ── Table columns ───────────────────────────────────────────────────────────
  readonly tableColumns = [
    { key: 'title', label: 'library.table.title', sortable: true },
    { key: 'status', label: 'libraryIntel.status.label', sortable: false },
    { key: 'primaryVideoCodec', label: 'library.table.videoCodec', sortable: true },
    { key: 'primaryAudioCodec', label: 'library.table.audioCodec', sortable: true },
    { key: 'primaryResolution', label: 'library.table.resolution', sortable: true },
    { key: 'fileCount', label: 'library.table.fileCount', sortable: true },
    { key: 'totalSizeBytes', label: 'library.table.totalSize', sortable: true },
    { key: 'avgEpisodeSizeBytes', label: 'library.table.avgEpSize', sortable: true },
    { key: 'gibPerHour', label: 'library.table.gibPerHour', sortable: true },
    { key: 'primaryBitDepth', label: 'library.table.bitDepth', sortable: true },
  ];

  // ── Codec match-mode popover ────────────────────────────────────────────────
  readonly matchModeOpen = signal(false);

  @ViewChild('matchModeHost') private matchModeHost?: ElementRef<HTMLElement>;

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const host = this.matchModeHost?.nativeElement;
    if (this.matchModeOpen() && host && !host.contains(event.target as Node)) {
      this.matchModeOpen.set(false);
    }
  }

  // ── Computed: filtered + sorted rows ────────────────────────────────────────
  readonly filteredRows = computed(() => {
    const f = this.filters();
    let result = [...this.rows()];

    // Title filter
    const title = f.title.toLowerCase().trim();
    if (title) {
      result = result.filter(r => r.title.toLowerCase().includes(title));
    }

    // Video codec filter — match-mode/threshold semantics over the codec pills.
    if (f.videoCodec.length) {
      result = result.filter(r => this.matchesVideoCodecSelection(r, f.videoCodec, f.codecThreshold));
    }

    // Audio codec filter — distribution-aware, primary codec fallback.
    const aCodecs = f.audioCodec;
    if (aCodecs.length) {
      result = result.filter(r => {
        if (r.audioCodecDistribution) {
          return aCodecs.some(c => Object.keys(r.audioCodecDistribution!).some(k => k.toUpperCase() === c));
        }
        return r.primaryAudioCodec != null && aCodecs.includes(r.primaryAudioCodec);
      });
    }

    // Resolution filter — same distribution-aware logic.
    const res = f.resolution;
    if (res.length) {
      result = result.filter(r => {
        if (r.resolutionDistribution) {
          return res.some(rv => Object.keys(r.resolutionDistribution!).some(k => k.toUpperCase() === rv.toUpperCase()));
        }
        return r.primaryResolution != null && res.includes(r.primaryResolution);
      });
    }

    // Split Codecs Only — series with more than one distinct video codec.
    if (f.splitCodecsOnly) {
      result = result.filter(r => isSplitCodec(r));
    }

    // Avg episode size threshold — numeric string in MB ('' = off), >= comparison.
    const avgMb = parseFloat(f.avgEpSize);
    if (f.avgEpSize.trim() && !Number.isNaN(avgMb)) {
      result = result.filter(r => r.avgEpisodeSizeBytes >= avgMb * 1024 * 1024);
    }

    // GiB/h threshold — numeric string ('' = off), >= comparison.
    const gib = parseFloat(f.gibPerHour);
    if (f.gibPerHour.trim() && !Number.isNaN(gib)) {
      result = result.filter(r => (r.gibPerHour ?? 0) >= gib);
    }

    // Season-aware status filter (transcodeReady = ended-complete OR season-hiatus).
    if (f.status !== 'all') {
      result = result.filter(r =>
        f.status === 'transcodeReady' ? isTranscodeReady(r) : deriveSeriesStatus(r) === f.status,
      );
    }

    // Download % filter
    if (f.download === '100') result = result.filter(r => r.percentDownloaded >= 100);
    if (f.download === 'partial') result = result.filter(r => r.percentDownloaded < 100 && r.percentDownloaded > 0);

    // Bit depth filter
    if (f.bitDepth) result = result.filter(r => r.primaryBitDepth === +f.bitDepth);

    // With Files filter
    if (f.hasFiles === 'yes') result = result.filter(r => (r.fileCount ?? 0) > 0);
    if (f.hasFiles === 'no') result = result.filter(r => (r.fileCount ?? 0) === 0);

    // Sort
    const col = f.sortCol;
    const dir = f.sortDir;
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

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.filteredRows().length / this.filters().pageSize)),
  );

  readonly pagedRows = computed(() => {
    const size = this.filters().pageSize;
    const current = Math.min(this.filters().page, this.totalPages());
    const start = (current - 1) * size;
    return this.filteredRows().slice(start, start + size);
  });

  readonly pageRange = computed<(number | '...')[]>(() => {
    const total = this.totalPages();
    const current = Math.min(this.filters().page, total);
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
    const f = this.filters();
    let count = 0;
    if (f.title.trim()) count++;
    count += f.videoCodec.length;
    count += f.audioCodec.length;
    count += f.resolution.length;
    if (f.codecThreshold.mode !== 'any') count++; // 'strict' / 'mixed' / 'custom' each count once
    if (f.splitCodecsOnly) count++;
    if (f.avgEpSize.trim()) count++;
    if (f.gibPerHour.trim()) count++;
    if (f.status !== 'all') count++;
    if (f.download !== 'all') count++;
    if (f.bitDepth) count++;
    if (f.hasFiles !== 'all') count++;
    return count;
  });

  // ── Methods ─────────────────────────────────────────────────────────────────

  // ── ui-select option sets (labels translated here — the component takes final strings) ──

  thresholdOpSelectOptions(): UiSelectOption[] {
    return [
      { label: this.translateService.instant('libraryIntel.matchMode.opGte'), value: 'gte' },
      { label: this.translateService.instant('libraryIntel.matchMode.opLte'), value: 'lte' },
    ];
  }

  avgEpSizeSelectOptions(): UiSelectOption[] {
    return [
      { label: this.translateService.instant('library.filters.all'), value: '' },
      ...this.AVG_EP_SIZES.map(o => ({ label: o.label, value: o.value })),
    ];
  }

  gibPerHourSelectOptions(): UiSelectOption[] {
    return [
      { label: this.translateService.instant('library.filters.all'), value: '' },
      ...this.GIB_PER_HOUR_THRESHOLDS.map(o => ({ label: o.label, value: o.value })),
    ];
  }

  statusSelectOptions(): UiSelectOption[] {
    return this.STATUS_OPTIONS.map(opt => ({
      label: this.translateService.instant(this.STATUS_LABELS[opt]),
      value: opt,
    }));
  }

  downloadSelectOptions(): UiSelectOption[] {
    return [
      { label: this.translateService.instant('library.filters.all'), value: 'all' },
      { label: '100%', value: '100' },
      { label: this.translateService.instant('library.filters.partial'), value: 'partial' },
    ];
  }

  bitDepthSelectOptions(): UiSelectOption[] {
    return [
      { label: this.translateService.instant('library.filters.all'), value: '' },
      { label: '8-bit', value: '8' },
      { label: '10-bit', value: '10' },
    ];
  }

  hasFilesSelectOptions(): UiSelectOption[] {
    return [
      { label: this.translateService.instant('library.filters.all'), value: 'all' },
      { label: this.translateService.instant('library.filters.withFilesYes'), value: 'yes' },
      { label: this.translateService.instant('library.filters.withFilesNo'), value: 'no' },
    ];
  }

  pageSizeSelectOptions(): UiSelectOption[] {
    return this.pageSizeOptions.map(o => ({ label: String(o), value: o }));
  }

  /** Applies a partial filter patch against the shared store state and resets paging. */
  setFilter(patch: Partial<LibraryFilterState>): void {
    this.store.filters.update(f => ({
      ...f,
      ...patch,
      codecThreshold: patch.codecThreshold ?? f.codecThreshold,
      page: 1,
    }));
  }

  togglePill(key: 'videoCodec' | 'audioCodec' | 'resolution', value: string): void {
    const f = this.filters();
    const next = f[key].includes(value) ? f[key].filter(v => v !== value) : [...f[key], value];
    if (key === 'videoCodec') {
      if (!next.length) this.matchModeOpen.set(false);
      this.setFilter({ videoCodec: next });
    } else if (key === 'audioCodec') this.setFilter({ audioCodec: next });
    else this.setFilter({ resolution: next });
  }

  toggleSplitCodecsOnly(): void {
    this.setFilter({ splitCodecsOnly: !this.filters().splitCodecsOnly });
  }

  setMatchMode(mode: CodecMatchMode): void {
    this.setFilter({ codecThreshold: { ...this.filters().codecThreshold, mode } });
  }

  setThresholdPercent(raw: string): void {
    const n = Number(raw);
    const percent = Number.isFinite(n) ? Math.min(100, Math.max(0, n)) : 0;
    this.setFilter({ codecThreshold: { ...this.filters().codecThreshold, percent } });
  }

  setThresholdOp(raw: string): void {
    const op: CodecPercentOp = raw === 'lte' ? 'lte' : 'gte';
    this.setFilter({ codecThreshold: { ...this.filters().codecThreshold, op } });
  }

  toggleSort(col: string): void {
    const f = this.filters();
    const dir = f.sortCol === col ? (f.sortDir === 'asc' ? 'desc' : 'asc') : 'desc';
    this.setFilter({ sortCol: col, sortDir: dir });
  }

  setPageSize(size: number): void {
    this.store.filters.update(f => ({ ...f, pageSize: size, page: 1 }));
  }

  goToPage(p: number | '...'): void {
    if (p === '...') return;
    const target = Math.max(1, Math.min(p, this.totalPages()));
    this.store.filters.update(f => ({ ...f, page: target }));
  }

  clearFilters(): void {
    this.store.resetFilters();
    this.matchModeOpen.set(false);
  }

  /** Host preset entry point (KPI chips): resets filters, then delegates to the store patch. */
  applyPreset(preset: Partial<LibraryFilterState>): void {
    this.store.resetFilters();
    this.store.applyFilterPatch(preset);
    this.matchModeOpen.set(false);
  }

  /** Row status badge label key (season-aware vocabulary). */
  statusBadgeKey(row: LibrarySeriesRow): string {
    return `libraryIntel.badge.${deriveSeriesStatus(row)}`;
  }

  /** Row status badge tooltip key — the full explanation behind the compact badge label. */
  statusBadgeTipKey(row: LibrarySeriesRow): string {
    return `libraryIntel.badgeTip.${deriveSeriesStatus(row)}`;
  }

  statusBadgeClass(row: LibrarySeriesRow): string {
    const status: SeriesAirStatus = deriveSeriesStatus(row);
    switch (status) {
      case 'seriesEndedComplete': return 'la-status-badge--ended';
      case 'incompleteOnDisk': return 'la-status-badge--incomplete';
      case 'seasonEndedHiatus':
      case 'airingWeekly': return 'la-status-badge--amber';
      case 'transcodeReady': return 'la-status-badge--ended'; // unreachable: deriveSeriesStatus never emits it
    }
  }

  /**
   * Codec Threshold Filtering — s_i = codecFileShare(row, c_i) for each selected codec:
   *  - any    : max(s_i) > 0
   *  - strict : sum(s_i) >= 99.9
   *  - mixed  : min(s_i) > 0 AND sum(s_i) < 99.9
   *  - custom : max(s_i) satisfies the configured op / percent
   * Un-enriched rows (no videoCodecDistribution) fall back to primaryVideoCodec
   * (share 100 if selected, else 0).
   */
  private matchesVideoCodecSelection(
    row: LibrarySeriesRow,
    codecs: string[],
    threshold: CodecThreshold,
  ): boolean {
    const share = (codec: string): number => {
      if (!row.videoCodecDistribution) {
        return (row.primaryVideoCodec ?? '').trim().toUpperCase() === codec.trim().toUpperCase() ? 100 : 0;
      }
      return codecFileShare(row, codec);
    };

    const shares = codecs.map(share);
    const max = Math.max(...shares);
    const sum = shares.reduce((a, b) => a + b, 0);
    const min = Math.min(...shares);

    switch (threshold.mode) {
      case 'any': return matchesCodecThreshold(max, threshold);
      case 'strict': return sum >= 99.9;
      case 'mixed': return min > 0 && sum < 99.9;
      case 'custom': return matchesCodecThreshold(max, threshold);
    }
  }

  formatSize(bytes: number | undefined): string {
    return formatTableSize(bytes, key => this.translateService.instant(key));
  }

  /**
   * 'libraryIntel.matchMode.splitOnly' ships with a leading emoji glyph — the design
   * replaces it with an inline SVG bolt, so strip decorative glyphs from the label.
   */
  splitOnlyLabel(): string {
    return this.translateService
      .instant('libraryIntel.matchMode.splitOnly')
      .replace(/[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}\u{FE0F}]/gu, '')
      .trim();
  }

  // ── Audio codec pills (tone-coded, <5% entries folded into a +N badge) ──────

  /** Tone bucket for an audio codec: AAC/MP3 → slate, OPUS → teal, EAC3/AC3 → purple, FLAC/WAV/DTS → amber. */
  audioPillTone(label: string): string {
    const key = label.toUpperCase().replace(/[^A-Z0-9]/g, '');
    if (key.includes('AC3')) return 'purple'; // AC3 / EAC3 / E-AC-3
    if (key.startsWith('OPUS')) return 'teal';
    if (key.startsWith('DTS') || key.startsWith('FLAC') || key.startsWith('WAV')) return 'amber';
    return 'slate'; // AAC, MP3, and anything unrecognised
  }

  /**
   * Split the audio distribution at the 5% share line: major codecs render as tone
   * pills, the rest collapse into ONE compact +N badge so row height stays stable.
   * If every entry is tiny, the dominant codec stays visible so the cell never
   * renders as a bare "+N".
   */
  audioPills(row: LibrarySeriesRow): {
    visible: { label: string; pct: number; tone: string }[];
    hiddenCount: number;
  } {
    const dist = row.audioCodecDistribution;
    const pills = getCodecPills(dist);
    const total = dist ? Object.values(dist).reduce((s, v) => s + Math.max(0, v), 0) : 0;
    const shareOf = (label: string): number => {
      const count = dist ? (Object.entries(dist).find(([k]) => k.toUpperCase() === label)?.[1] ?? 0) : 0;
      return total > 0 ? (Math.max(0, count) / total) * 100 : 0;
    };

    const visible = pills
      .filter(p => shareOf(p.label) >= 5)
      .map(p => ({ label: p.label, pct: p.pct, tone: this.audioPillTone(p.label) }));

    if (!visible.length && pills.length) {
      const top = pills[0];
      return {
        visible: [{ label: top.label, pct: top.pct, tone: this.audioPillTone(top.label) }],
        hiddenCount: pills.length - 1,
      };
    }
    return { visible, hiddenCount: pills.length - visible.length };
  }
}

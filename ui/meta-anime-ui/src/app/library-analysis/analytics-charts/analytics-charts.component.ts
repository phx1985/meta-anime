import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { map, merge } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SegmentedControlComponent, SegmentedOption } from '../../shared/ui/segmented-control.component';
import { UiToggleComponent } from '../../shared/ui/ui-toggle/ui-toggle.component';
import { UiBadgeComponent, UiBadgeColor } from '../../shared/ui/ui-badge.component';
import { UiCardComponent } from '../../shared/ui/ui-card.component';
import { TelemetryBoxComponent } from '../../shared/ui/telemetry-box.component';
import { ThemeService } from '../../services/theme.service';
import { readToken } from '../../shared/chart-theme';
import { AnimeService } from '../../services/anime.service';
import { UserPathService } from '../../services/user-path.service';
import { buildHandBrakeQueue, serializeHandBrakeQueue } from '../../utils/user-path.utils';
import { LibraryIntelStore } from '../../services/library-intel-store.service';
import { LibrarySeriesRow } from '../../models/library-analysis.models';
import {
  BLOAT_GIB_PER_HOUR,
  DonutMode,
  DonutSlice,
  LeaderboardEntry,
  LeaderboardMode,
  LibraryFilterState,
  buildCodecDonut,
  buildLeaderboard,
  estimateTranscodeSavings,
  libraryAvgGibPerHour,
  collectUntranscodedFiles,
  sortBatchFiles,
  batchQueueFileName,
  estimateBatchFreedGb,
  BatchOrder,
} from '../../models/library-intel.models';
import {
  Chart,
  ArcElement,
  DoughnutController,
  Tooltip,
  Legend,
  ChartConfiguration,
} from 'chart.js';
// Slice-percentage datalabels were dropped in the restyle — the donut hole and
// the HTML breakdown list carry those numbers now.
Chart.register(
  ArcElement,
  DoughnutController,
  Tooltip,
  Legend,
);

/** Binary units — the whole page formats sizes the same way. */
const GIB = 1024 ** 3;
const TIB = 1024 ** 4;

/** Stable preset ids shared with the store's activePresetId highlight state. */
export type QuickFilterPresetId = 'prime' | 'unfinished' | 'completeH264';

export interface QuickFilterPreset {
  id: QuickFilterPresetId;
  labelKey: string;
  variant: 'prime' | 'unfinished' | 'complete';
  patch: Partial<LibraryFilterState>;
}

@Component({
  selector: 'app-la-analytics-charts',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, UiCardComponent, UiBadgeComponent, SegmentedControlComponent, TelemetryBoxComponent, UiToggleComponent],
  templateUrl: './analytics-charts.component.html',
  styleUrl: './analytics-charts.component.scss',
})
export class AnalyticsChartsComponent implements OnDestroy {
  private readonly themeService = inject(ThemeService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly store = inject(LibraryIntelStore);
  private readonly animeService = inject(AnimeService);
  readonly userPaths = inject(UserPathService);

  // ── Input signals ──────────────────────────────────────────────────────────

  readonly rows = input<LibrarySeriesRow[]>([]);
  readonly isLoading = input<boolean>(false);
  readonly isEnriching = input<boolean>(false);

  // ── Output signals ─────────────────────────────────────────────────────────

  /** Open a series in the detail drawer (Sonarr id). */
  readonly openSeries = output<number>();

  // ── Toggle state ───────────────────────────────────────────────────────────

  readonly donutMode = signal<DonutMode>('count');
  readonly leaderboardMode = signal<LeaderboardMode>('totalGb');

  /** Bumps whenever loaded translations change so option labels re-derive. */
  private readonly translationsVersion = toSignal(
    merge(this.translate.onLangChange, this.translate.onTranslationChange).pipe(map((_, i) => i + 1)),
    { initialValue: 0 },
  );

  /** Segmented-control options for the donut view switcher. */
  readonly donutModeOptions = computed<SegmentedOption[]>(() => {
    this.translationsVersion();
    return [
      { id: 'count', label: this.translate.instant('libraryIntel.buddy.byCount') },
      { id: 'size', label: this.translate.instant('libraryIntel.buddy.bySize') },
    ];
  });

  /** Segmented-control options for the storage-hogs leaderboard mode. */
  readonly leaderboardModeOptions = computed<SegmentedOption[]>(() => {
    this.translationsVersion();
    return [
      { id: 'totalGb', label: this.translate.instant('libraryIntel.buddy.topTotalShort') },
      { id: 'bloatGibPerHour', label: this.translate.instant('libraryIntel.buddy.topBloatShort') },
      { id: 'reclaimable', label: this.translate.instant('libraryIntel.buddy.topReclaimShort') },
    ];
  });

  /** Segmented-control options for the batch SORT control (copy in styled tips). */
  readonly batchSortOptions = computed<SegmentedOption[]>(() => {
    this.translationsVersion();
    return [
      {
        id: 'biggest',
        label: this.translate.instant('libraryIntel.buddy.batchSortBitrate'),
        tip: this.translate.instant('libraryIntel.buddy.batchOrderBiggest'),
      },
      {
        id: 'series',
        label: this.translate.instant('libraryIntel.buddy.batchSortAlpha'),
        tip: this.translate.instant('libraryIntel.buddy.batchOrderSeries'),
      },
    ];
  });

  // ── Derived view models ────────────────────────────────────────────────────

  readonly hasData = computed(() => this.rows().length > 0);

  /** Total files across rows — the "Analyzing {{count}} files…" badge count. */
  readonly totalFileCount = computed(() =>
    this.rows().reduce((sum, row) => sum + (row.fileCount ?? 0), 0),
  );

  /**
   * True while library telemetry is still arriving: an explicit fetch/enrich pass,
   * or rows that exist but not one of them is enriched yet (no episode files and
   * GiB/h still 0). Gates every numeric readout so the cards never flash zeros.
   */
  readonly isAnalyticsLoading = computed(() => {
    if (this.isLoading() || this.isEnriching()) return true;
    const rows = this.rows();
    return (
      rows.length > 0 &&
      rows.every((row) => !row.episodeFiles?.length && (row.gibPerHour ?? 0) === 0)
    );
  });

  readonly donutSlices = computed(() => buildCodecDonut(this.rows(), this.donutMode()));
  readonly leaderboard = computed(() => buildLeaderboard(this.rows(), this.leaderboardMode(), 5, this.userPaths.includeUnknown()));

  /** Mini poster URLs per leaderboard row (null = placeholder), fetched lazily. */
  private readonly posterBySonarr = signal<Record<number, string | null>>({});

  posterUrl(entry: LeaderboardEntry): string | null {
    return this.posterBySonarr()[entry.sonarrId] ?? null;
  }

  /** Hover zoom preview for the mini posters (fixed-position, follows the row). */
  readonly posterPreview = signal<{ url: string; x: number; y: number } | null>(null);

  onPosterEnter(event: Event, entry: LeaderboardEntry): void {
    const url = this.posterUrl(entry);
    if (!url) return;
    const me = event as MouseEvent;
    this.posterPreview.set({ url, ...this.previewPosition(me.clientX, me.clientY) });
  }

  /** Keep the preview glued to the cursor while it moves within the tile. */
  onPosterMove(event: Event): void {
    const current = this.posterPreview();
    if (!current) return;
    const me = event as MouseEvent;
    this.posterPreview.set({ url: current.url, ...this.previewPosition(me.clientX, me.clientY) });
  }

  /** Cursor-relative, viewport-clamped preview position (magnifier style). */
  private previewPosition(cx: number, cy: number): { x: number; y: number } {
    const w = 220;
    const h = 320;
    const x = Math.max(8, Math.min(cx + 18, window.innerWidth - w - 8));
    const y = Math.max(8, Math.min(cy - h / 2, window.innerHeight - h - 8));
    return { x, y };
  }

  onPosterLeave(): void {
    this.posterPreview.set(null);
  }

  /** Loading (url lookup or image decode still in flight). */
  isPosterPending(entry: LeaderboardEntry): boolean {
    return !(entry.sonarrId in this.posterBySonarr());
  }

  onPosterImgLoad(id: number): void {
    this.posterLoaded.update(m => ({ ...m, [id]: true }));
  }

  onPosterImgError(entry: LeaderboardEntry): void {
    this.posterBySonarr.update(c => ({ ...c, [entry.sonarrId]: null }));
  }

  /** Initials fallback tile for series without a poster. */
  initials(title: string): string {
    const words = title.trim().split(/\s+/).slice(0, 2);
    return words.map(w => w[0]?.toUpperCase() ?? '').join('') || '?';
  }
  readonly avgGibPerHour = computed(() => libraryAvgGibPerHour(this.rows()));
  readonly footprintBytes = computed(
    () => this.rows().reduce((a, r) => a + (r.totalSizeBytes ?? 0), 0),
  );

  // ── Donut hole hero + structured legend (replaces floating Chart.js labels) ──

  /** Dominant codec share centered in the donut hole ("94%" + "HEVC"). */
  readonly donutDominant = computed(() => {
    const slices = this.donutSlices().slice(0, 15);
    const top = slices[0];
    const total = slices.reduce((a, s) => a + s.value, 0);
    return {
      pct: top && total > 0 ? this.pctLabel(top.value, total) : '—',
      label: top?.label ?? '',
    };
  });

  /** Legend rows: dot color + codec + file count + share pill (mode-aware %). */
  readonly donutLegend = computed(() => {
    const slices = this.donutSlices().slice(0, 15);
    const total = slices.reduce((a, s) => a + s.value, 0);
    const counts = this.codecCounts();
    return slices.map((s, i) => ({
      label: s.label,
      color: this.PALETTE[i % this.PALETTE.length],
      count: counts[s.label] ?? 0,
      pct: total > 0 ? this.pctLabel(s.value, total) : '0%',
    }));
  });

  /**
   * Text alternative for the donut canvas (role="img" aria-label): the same
   * per-codec shares the HTML legend renders, in reading order.
   */
  readonly donutAriaLabel = computed(() => {
    this.translationsVersion();
    const summary = this.donutLegend()
      .map((r) => `${r.label} ${r.pct} (${this.formatCount(r.count)})`)
      .join(', ');
    return this.translate.instant('libraryIntel.buddy.chartAria', { summary });
  });

  /** File counts per codec label (mirrors buildCodecDonut's aggregation). */
  private readonly codecCounts = computed<Record<string, number>>(() => {
    const counts: Record<string, number> = {};
    const bump = (key: string, n: number): void => {
      counts[key] = (counts[key] ?? 0) + n;
    };
    for (const row of this.rows()) {
      const files = row.episodeFiles;
      if (files?.length) {
        for (const f of files) bump(this.normCodec(f.videoCodec) || 'UNKNOWN', 1);
        continue;
      }
      for (const [codec, count] of Object.entries(row.videoCodecDistribution ?? {})) {
        bump(this.normCodec(codec) || 'UNKNOWN', count);
      }
    }
    return counts;
  });

  /** Same codec normalization buildCodecDonut applies (x264 → H264). */
  private normCodec(codec: string | null | undefined): string {
    return (codec ?? '').trim().toUpperCase().replace('X264', 'H264');
  }

  /** "94%" / "0.42%" share label for a donut slice. */
  private pctLabel(value: number, total: number): string {
    const raw = total > 0 ? (value / total) * 100 : 0;
    const pct = raw < 1 && raw > 0 ? raw.toFixed(2) : raw.toFixed(0);
    return `${pct}%`;
  }

  readonly savings = computed(() => estimateTranscodeSavings(this.rows(), this.userPaths.includeUnknown()));
  readonly savingsGb = computed(() => Math.round(this.savings().reclaimableBytes / GIB));
  readonly savingsCount = computed(() => this.savings().seriesCount);

  // ── Quick-filter presets (reset-then-apply via LibraryIntelStore.togglePreset) ──

  /** Id of the preset currently driving the filters (bright ring on its chip). */
  readonly activePresetId = this.store.activePresetId;

  /** Chip that just applied its preset (brief "Applied" checkmark flash). */
  readonly flashPresetId = signal<QuickFilterPresetId | null>(null);
  private flashTimer: ReturnType<typeof setTimeout> | null = null;

  readonly presets: readonly QuickFilterPreset[] = [
    {
      id: 'prime',
      labelKey: 'libraryIntel.buddy.chipsPrime',
      variant: 'prime',
      patch: {
        videoCodec: ['H264'],
        codecThreshold: { mode: 'custom', percent: 50, op: 'gte' },
        gibPerHour: '2',
        status: 'transcodeReady',
      },
    },
    {
      id: 'unfinished',
      labelKey: 'libraryIntel.buddy.chipsUnfinished',
      variant: 'unfinished',
      patch: {
        splitCodecsOnly: true,
        videoCodec: ['H264'],
        codecThreshold: { mode: 'any', percent: 0, op: 'gte' },
      },
    },
    {
      id: 'completeH264',
      labelKey: 'libraryIntel.buddy.chipsCompleteH264',
      variant: 'complete',
      patch: {
        videoCodec: ['H264'],
        codecThreshold: { mode: 'any', percent: 0, op: 'gte' },
        status: 'transcodeReady',
        download: '100',
      },
    },
  ];

  // ── Canvas ref & chart instance ────────────────────────────────────────────

  readonly codecCanvas = viewChild<ElementRef<HTMLCanvasElement>>('codecCanvas');
  private codecChart: Chart | null = null;

  // ── Color palette (matches GenreChartComponent) ────────────────────────────

  private readonly PALETTE = [
    '#6c63ff',
    '#3ecfcf',
    '#e5a00d',
    '#ef4444',
    '#22c55e',
    '#3b82f6',
    '#a855f7',
    '#ec4899',
    '#f97316',
    '#14b8a6',
    '#84cc16',
    '#06b6d4',
    '#f59e0b',
    '#8b5cf6',
    '#10b981',
  ];

  /** In-flight poster lookups (dedupe guard; not reactive). */
  private readonly posterPending = new Set<number>();
  /** True once the <img> has decoded — drives the fade-in. */
  private readonly posterLoaded = signal<Record<number, boolean>>({});

  constructor() {
    // Cache semantics: missing key = loading (shimmer tile), null = no poster
    // (initials tile), string = url (fade-in image once decoded).
    effect(() => {
      const entries = this.leaderboard();
      const cache = this.posterBySonarr();
      const rows = this.rows();
      for (const e of entries) {
        if (e.sonarrId in cache || this.posterPending.has(e.sonarrId)) continue;
        const localId = rows.find(r => r.sonarrId === e.sonarrId)?.localAnimeId;
        if (!localId) {
          this.posterBySonarr.update(c => ({ ...c, [e.sonarrId]: null }));
          continue;
        }
        this.posterPending.add(e.sonarrId);
        this.animeService.getAnime(localId).subscribe({
          next: a => {
            const url = a.malAnimes?.[0]?.coverImageUrl ?? null;
            this.posterBySonarr.update(c => ({ ...c, [e.sonarrId]: url }));
          },
          error: () => {
            this.posterBySonarr.update(c => ({ ...c, [e.sonarrId]: null }));
          },
        });
      }
    });

    // Keep the custom N inside the pool when it shrinks (e.g. unknown-codec toggle).
    effect(() => {
      const total = this.batchPool().length;
      if (this.batchCustomN() > total) {
        this.batchCustomN.set(Math.max(1, total));
      }
    });

    // Rebuild the codec donut when rows, mode or canvas availability change
    effect(() => {
      const slices = this.donutSlices();
      const mode = this.donutMode();
      const canvas = this.codecCanvas();
      if (!canvas) {
        this.codecChart?.destroy();
        this.codecChart = null;
        return;
      }
      if (slices.length > 0) {
        this.codecChart = this.buildDoughnutChart(canvas.nativeElement, slices, mode, this.codecChart);
      }
    });

    // Rebuild the donut when the active language changes
    this.translate.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.rebuildChart());
  }

  ngOnDestroy(): void {
    this.codecChart?.destroy();
    if (this.flashTimer) clearTimeout(this.flashTimer);
  }

  // ── Toggle handlers ────────────────────────────────────────────────────────

  setDonutMode(mode: DonutMode): void {
    this.donutMode.set(mode);
  }

  // ── Global transcode batch (next N untranscoded episodes) ────────────────
  readonly batchCountChoice = signal<number | 'all' | 'custom'>(10);
  readonly batchCustomN = signal<number>(25);
  readonly batchOrder = signal<BatchOrder>('biggest');

  readonly batchN = computed(() => {
    const choice = this.batchCountChoice();
    const total = this.batchPool().length;
    if (choice === 'all') return total;
    return choice === 'custom'
      ? Math.min(Math.max(1, Math.floor(this.batchCustomN()) || 1), total)
      : choice;
  });

  /** Every transcode target in the library — no series/season state gating. */
  readonly batchPool = computed(() =>
    sortBatchFiles(
      collectUntranscodedFiles(
        this.rows(),
        p => this.userPaths.resolve(p),
        this.userPaths.includeUnknown()),
      this.batchOrder()));

  readonly batchSelection = computed(() => this.batchPool().slice(0, this.batchN()));

  readonly batchEstimateGb = computed(() =>
    (this.batchSelection().reduce((a, f) => a + f.sizeBytes, 0) / 1_073_741_824).toFixed(1));

  /** Estimated space freed after transcoding the selection to H265. */
  readonly batchFreedGb = computed(() => estimateBatchFreedGb(this.batchSelection()).toFixed(1));

  /** Post-transcode target size (selection × HEVC_SIZE_FACTOR). */
  readonly batchFinalGb = computed(() =>
    (this.batchSelection().reduce((a, f) => a + f.sizeBytes, 0) * 0.45 / 1_073_741_824).toFixed(1));

  /** Freed space as % of the selected originals. */
  readonly batchFreedPct = computed(() => {
    const sel = this.batchSelection();
    const total = sel.reduce((a, f) => a + f.sizeBytes, 0);
    return total > 0 ? Math.round((estimateBatchFreedGb(sel) * 1_073_741_824 / total) * 100) : 0;
  });

  setBatchCount(choice: number | 'all' | 'custom'): void {
    this.batchCountChoice.set(choice);
  }

  /** Live typing: selection follows, clamping happens in batchN. */
  onBatchCustomChange(event: Event): void {
    this.batchCountChoice.set('custom');
    const n = Math.floor(Number((event.target as HTMLInputElement).value)) || 1;
    this.batchCustomN.set(n);
  }

  /** On blur/enter: strictly clamp into [1, total] and normalize the field. */
  onBatchCustomCommit(event: Event): void {
    const input = event.target as HTMLInputElement;
    const total = Math.max(1, this.batchPool().length);
    const n = Math.max(1, Math.min(Math.floor(Number(input.value)) || 1, total));
    this.batchCustomN.set(n);
    input.value = String(n);
  }

  setBatchOrder(order: BatchOrder): void {
    this.batchOrder.set(order);
  }

  toggleIncludeUnknown(): void {
    this.userPaths.saveTranscode({ includeUnknown: !this.userPaths.includeUnknown() }).subscribe();
  }

  /** Downloads one HandBrake queue.json for the selected batch. */
  exportBatch(): void {
    const files = this.batchSelection();
    if (!files.length) return;
    const queue = buildHandBrakeQueue(
      files,
      this.userPaths.destDir(),
      this.userPaths.destMode(),
      this.userPaths.presetName());
    const blob = new Blob([serializeHandBrakeQueue(queue)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = batchQueueFileName();
    a.click();
    URL.revokeObjectURL(url);
  }

  setLeaderboardMode(mode: LeaderboardMode): void {
    this.leaderboardMode.set(mode);
  }

  /**
   * Quick-filter chip click: the store resets ALL filters before applying the
   * preset's patch; clicking the active preset again clears and deactivates it.
   */
  applyPreset(preset: QuickFilterPreset): void {
    this.store.togglePreset(preset.id, preset.patch);
    if (this.store.activePresetId() !== preset.id) return; // toggled off — no flash
    this.flashPresetId.set(preset.id);
    if (this.flashTimer) clearTimeout(this.flashTimer);
    this.flashTimer = setTimeout(() => this.flashPresetId.set(null), 1500);
  }

  // ── Public helpers for template ────────────────────────────────────────────

  formatSize(bytes: number): string {
    if (bytes >= TIB) return (bytes / TIB).toFixed(1) + ' TB';
    return (bytes / GIB).toFixed(1) + ' GB';
  }

  /** File counts with locale grouping ("12,665"). */
  formatCount(n: number): string {
    return n.toLocaleString();
  }

  /** Multi-codec strings ("HEVC/UNKNOWN/H264") collapse into a 'Mixed (n)' pill. */
  isMixedCodec(badge: string): boolean {
    return (badge ?? '').includes('/');
  }

  /** Number of distinct codecs in a split-codec badge. */
  codecPartCount(badge: string): number {
    return (badge ?? '').split('/').length;
  }

  /** Semantic GiB/h badge: emerald = lean, amber = bloaty, crimson = extreme. */
  gibBadgeColor(gib: number): UiBadgeColor {
    if (gib >= BLOAT_GIB_PER_HOUR * 2) return 'danger';
    if (gib > BLOAT_GIB_PER_HOUR) return 'warning';
    return 'success';
  }

  /** Semantic codec badge: H264 = transcode candidate (amber), HEVC/AV1 = optimized (emerald). */
  codecBadgeColor(badge: string): UiBadgeColor {
    const b = (badge ?? '').toUpperCase();
    if (!b || b === '—') return 'neutral';
    if (b.includes('H264')) return b.includes('/') ? 'mixed' : 'warning';
    return 'success';
  }

  /** Value shown on the right of a leaderboard row for the active mode. */
  lbValue(entry: LeaderboardEntry): string {
    switch (this.leaderboardMode()) {
      case 'totalGb':
        return this.formatSize(entry.totalSizeBytes);
      case 'bloatGibPerHour':
        return entry.gibPerHour.toFixed(2) + ' GiB/h';
      case 'reclaimable':
        return '~' + this.formatSize(entry.reclaimableBytes);
    }
  }

  /** Sum of the displayed rows' disk size (bottom summary bar). */
  readonly leaderboardFootprint = computed(() =>
    this.leaderboard().reduce((a, e) => a + e.totalSizeBytes, 0));

  /** Sum of the displayed rows' reclaimable bytes (bottom summary bar). */
  readonly leaderboardReclaimable = computed(() =>
    this.leaderboard().reduce((a, e) => a + e.reclaimableBytes, 0));

  /**
   * Width of the row's size meter — always the relative DISK SIZE against the
   * biggest row, whatever the sort mode (it's the "storage hog" bar; tying it
   * to the sort metric made rate-sorted rows draw arbitrary-looking widths).
   */
  lbBarPercent(entry: LeaderboardEntry): number {
    const entries = this.leaderboard();
    const max = entries.reduce((a, e) => Math.max(a, e.totalSizeBytes), 0);
    return max > 0 ? (entry.totalSizeBytes / max) * 100 : 0;
  }

  // ── Chart builder (Chart.js doughnut + datalabels) ─────────────────────────

  private buildDoughnutChart(
    canvas: HTMLCanvasElement,
    slices: DonutSlice[],
    mode: DonutMode,
    existing: Chart | null,
  ): Chart {
    existing?.destroy();

    const entries = slices.slice(0, 15);
    const labels = entries.map(s => s.label);
    const data = entries.map(s => s.value);
    const colors = labels.map((_, i) => this.PALETTE[i % this.PALETTE.length]);
    const fmt = (value: number): string =>
      mode === 'size' ? this.formatSize(value) : `${value}`;

    this.themeService.isDarkTheme(); // signal read: keeps chart rebuilds theme-tracked
    const textColor = readToken('--text-primary');

    const config: ChartConfiguration<'doughnut'> = {
      type: 'doughnut',
      data: {
        labels,
        datasets: [
          {
            data,
            backgroundColor: colors,
            // Transparent slice borders — no dark seams between wedges.
            borderWidth: 2,
            borderColor: 'transparent',
            hoverOffset: 12,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: {
          duration: AnalyticsChartsComponent.prefersReducedMotion() ? 0 : 900,
          easing: 'easeInOutQuart' as const,
        },
        cutout: '62%',
        plugins: {
          // Legend + slice % labels live in HTML now (donut hole & breakdown list).
          legend: { display: false },
          // chartjs-plugin-datalabels is registered GLOBALLY by genre-chart /
          // activity-stats — explicitly off here or it draws raw counts on the
          // ring (they collide at the top for the small slices).
          datalabels: { display: false },
          tooltip: {
            titleColor: textColor,
            bodyColor: textColor,
            callbacks: {
              label: (ctx) => this.tooltipLabel(ctx as never, fmt),
            },
          },
        },
      },
    };

    return new Chart(canvas, config as unknown as ChartConfiguration);
  }

  // ── Shared helpers ─────────────────────────────────────────────────────────

  /** Chart animation honors the user's reduced-motion preference. */
  private static prefersReducedMotion(): boolean {
    return (
      typeof window !== 'undefined' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches
    );
  }

  private tooltipLabel(
    ctx: { raw: unknown; dataset: { data: unknown[] } },
    fmt: (value: number) => string,
  ): string {
    const values = ctx.dataset.data as number[];
    const total = values.reduce((a, b) => a + b, 0);
    const value = ctx.raw as number;
    const raw = total > 0 ? (value / total) * 100 : 0;
    const pct = raw < 1 && raw > 0 ? raw.toFixed(2) : raw.toFixed(1);
    return ` ${fmt(value)} (${pct}%)`;
  }

  private rebuildChart(): void {
    const canvas = this.codecCanvas();
    const slices = this.donutSlices();
    if (canvas && slices.length > 0) {
      this.codecChart = this.buildDoughnutChart(
        canvas.nativeElement,
        slices,
        this.donutMode(),
        this.codecChart,
      );
    }
  }
}

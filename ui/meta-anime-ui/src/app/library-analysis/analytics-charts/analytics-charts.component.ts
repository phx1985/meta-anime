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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ThemeService } from '../../services/theme.service';
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
} from '../../models/library-intel.models';
import {
  Chart,
  ArcElement,
  DoughnutController,
  Tooltip,
  Legend,
  ChartConfiguration,
} from 'chart.js';
import ChartDataLabels, { Context } from 'chartjs-plugin-datalabels';

Chart.register(
  ArcElement,
  DoughnutController,
  Tooltip,
  Legend,
  ChartDataLabels,
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
  imports: [TranslatePipe],
  templateUrl: './analytics-charts.component.html',
  styleUrl: './analytics-charts.component.scss',
})
export class AnalyticsChartsComponent implements OnDestroy {
  private readonly themeService = inject(ThemeService);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly store = inject(LibraryIntelStore);

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
  readonly leaderboard = computed(() => buildLeaderboard(this.rows(), this.leaderboardMode(), 5));
  readonly avgGibPerHour = computed(() => libraryAvgGibPerHour(this.rows()));
  readonly footprintBytes = computed(
    () => this.rows().reduce((a, r) => a + (r.totalSizeBytes ?? 0), 0),
  );
  readonly savings = computed(() => estimateTranscodeSavings(this.rows()));
  readonly savingsGb = computed(() => Math.round(this.savings().reclaimableBytes / GIB));
  readonly savingsCount = computed(() => this.savings().seriesCount);

  // ── Quick-filter presets (reset-then-apply via LibraryIntelStore.togglePreset) ──

  /** Id of the preset currently driving the filters (bright ring on its chip). */
  readonly activePresetId = this.store.activePresetId;

  /** Chip that just applied its preset (brief "✓ Applied" flash). */
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

  constructor() {
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

  /** Semantic GiB/h badge: emerald = lean, amber = bloaty, crimson = extreme. */
  gibBadgeClass(gib: number): string {
    if (gib >= BLOAT_GIB_PER_HOUR * 2) return 'la-badge la-badge--bad';
    if (gib > BLOAT_GIB_PER_HOUR) return 'la-badge la-badge--warn';
    return 'la-badge la-badge--ok';
  }

  /** Semantic codec badge: H264 = transcode candidate (amber), HEVC/AV1 = optimized (emerald). */
  codecBadgeClass(badge: string): string {
    const b = (badge ?? '').toUpperCase();
    if (!b || b === '—') return 'la-badge';
    if (b.includes('H264')) return b.includes('/') ? 'la-badge la-badge--mixed' : 'la-badge la-badge--warn';
    return 'la-badge la-badge--ok';
  }

  /** Value shown on the right of a leaderboard row for the active mode. */
  lbValue(entry: LeaderboardEntry): string {
    switch (this.leaderboardMode()) {
      case 'totalGb':
        return this.formatSize(entry.totalSizeBytes);
      case 'bloatGibPerHour':
        return entry.gibPerHour.toFixed(2) + ' GiB/h';
      case 'reclaimable':
        return this.formatSize(entry.reclaimableBytes);
    }
  }

  /** Width of the row's background bar relative to the current mode's leader. */
  lbBarPercent(entry: LeaderboardEntry): number {
    const metric = (e: LeaderboardEntry): number => {
      switch (this.leaderboardMode()) {
        case 'totalGb':
          return e.totalSizeBytes;
        case 'bloatGibPerHour':
          return e.gibPerHour;
        case 'reclaimable':
          return e.reclaimableBytes;
      }
    };
    const entries = this.leaderboard();
    const max = entries.reduce((a, e) => Math.max(a, metric(e)), 0);
    return max > 0 ? (metric(entry) / max) * 100 : 0;
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
    const total = data.reduce((a, b) => a + b, 0);
    const colors = labels.map((_, i) => this.PALETTE[i % this.PALETTE.length]);
    const fmt = (value: number): string =>
      mode === 'size' ? this.formatSize(value) : `${value}`;

    const isDark = this.themeService.isDarkTheme();
    const textColor = isDark ? '#e8e8f0' : '#1a1a2e';

    const config: ChartConfiguration<'doughnut'> = {
      type: 'doughnut',
      data: {
        labels,
        datasets: [
          {
            data,
            backgroundColor: colors,
            borderWidth: 2,
            borderColor: isDark ? '#1a1a2e' : '#fff',
            hoverOffset: 12,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 900, easing: 'easeInOutQuart' as const },
        cutout: '58%',
        plugins: {
          legend: {
            display: true,
            position: 'right',
            labels: {
              color: textColor,
              font: { size: 11 },
              boxWidth: 12,
              padding: 10,
              generateLabels: (chart: Chart) => {
                const ds = chart.data.datasets[0];
                return (chart.data.labels as string[]).map((lbl, i) => ({
                  text: `${lbl} (${fmt((ds.data as number[])[i])})`,
                  fillStyle: (ds.backgroundColor as string[])[i],
                  strokeStyle: (ds.backgroundColor as string[])[i],
                  lineWidth: 0,
                  index: i,
                  hidden: false,
                  fontColor: textColor,
                }));
              },
            },
          },
          tooltip: {
            callbacks: {
              label: (ctx) => this.tooltipLabel(ctx as never, fmt),
            },
          },
          datalabels: {
            color: '#fff',
            font: { weight: 'bold' as const, size: 10 },
            formatter: (value: number) => {
              const raw = total > 0 ? (value / total) * 100 : 0;
              const pct = raw < 1 && raw > 0 ? raw.toFixed(2) : raw.toFixed(0);
              return `${pct}%`;
            },
            display: (ctx: Context) => {
              const pct = total > 0 ? (ctx.dataset.data[ctx.dataIndex] as number) / total : 0;
              return pct > 0.04;
            },
          } as never,
        },
      },
    };

    return new Chart(canvas, config as unknown as ChartConfiguration);
  }

  // ── Shared helpers ─────────────────────────────────────────────────────────

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

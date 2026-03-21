import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  effect,
  inject,
  input,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ThemeService } from '../../services/theme.service';
import {
  Chart,
  ArcElement,
  BarElement,
  BarController,
  DoughnutController,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  ChartConfiguration,
} from 'chart.js';
import ChartDataLabels, { Context } from 'chartjs-plugin-datalabels';

Chart.register(
  ArcElement,
  BarElement,
  BarController,
  DoughnutController,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  ChartDataLabels,
);

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

  // ── Input signals ──────────────────────────────────────────────────────────

  readonly videoCodecDist = input<Record<string, number>>({});
  readonly audioCodecDist = input<Record<string, number>>({});
  readonly resolutionDist = input<Record<string, number>>({});
  readonly topStorageHogs = input<{ title: string; totalSizeBytes: number }[]>([]);
  readonly isEnriching = input<boolean>(false);
  readonly isLoading = input<boolean>(false);

  // ── Canvas refs ────────────────────────────────────────────────────────────

  readonly videoCodecCanvas = viewChild<ElementRef<HTMLCanvasElement>>('videoCodecCanvas');
  readonly audioCodecCanvas = viewChild<ElementRef<HTMLCanvasElement>>('audioCodecCanvas');
  readonly resolutionCanvas = viewChild<ElementRef<HTMLCanvasElement>>('resolutionCanvas');

  // ── Chart instances ────────────────────────────────────────────────────────

  private videoCodecChart: Chart | null = null;
  private audioCodecChart: Chart | null = null;
  private resolutionChart: Chart | null = null;

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
    // Rebuild charts on video codec data change
    effect(() => {
      const dist = this.videoCodecDist();
      const canvas = this.videoCodecCanvas();
      if (canvas && Object.keys(dist).length > 0) {
        this.videoCodecChart = this.buildDoughnutChart(
          canvas.nativeElement,
          dist,
          this.videoCodecChart,
        );
      }
    });

    // Rebuild charts on audio codec data change
    effect(() => {
      const dist = this.audioCodecDist();
      const canvas = this.audioCodecCanvas();
      if (canvas && Object.keys(dist).length > 0) {
        this.audioCodecChart = this.buildDoughnutChart(
          canvas.nativeElement,
          dist,
          this.audioCodecChart,
        );
      }
    });

    // Rebuild charts on resolution data change
    effect(() => {
      const dist = this.resolutionDist();
      const canvas = this.resolutionCanvas();
      if (canvas && Object.keys(dist).length > 0) {
        this.resolutionChart = this.buildBarChart(
          canvas.nativeElement,
          dist,
          this.resolutionChart,
        );
      }
    });

    // Rebuild all charts when the active language changes
    this.translate.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.rebuildAllCharts());
  }

  ngOnDestroy(): void {
    this.videoCodecChart?.destroy();
    this.audioCodecChart?.destroy();
    this.resolutionChart?.destroy();
  }

  // ── Public helpers for template ────────────────────────────────────────────

  formatSize(bytes: number): string {
    const TiB = 1024 ** 4;
    const GiB = 1024 ** 3;
    if (bytes >= TiB) return (bytes / TiB).toFixed(1) + ' TB';
    return (bytes / GiB).toFixed(1) + ' GB';
  }

  hogBarPercent(bytes: number): number {
    const hogs = this.topStorageHogs();
    if (!hogs.length) return 0;
    const max = hogs[0].totalSizeBytes;
    return max > 0 ? (bytes / max) * 100 : 0;
  }

  // ── Chart builders ─────────────────────────────────────────────────────────

  private buildDoughnutChart(
    canvas: HTMLCanvasElement,
    dist: Record<string, number>,
    existing: Chart | null,
  ): Chart {
    existing?.destroy();

    const entries = Object.entries(dist)
      .map(([k, v]) => [k || 'Unknown', v] as [string, number])
      .sort((a, b) => b[1] - a[1])
      .slice(0, 15);
    const labels = entries.map(([k]) => k);
    const data = entries.map(([, v]) => v);
    const total = data.reduce((a, b) => a + b, 0);
    const colors = labels.map((_, i) => this.PALETTE[i % this.PALETTE.length]);

    const isDark = this.themeService.isDarkTheme();
    const textColor = isDark ? '#e8e8f0' : '#1a1a2e';

    const config: ChartConfiguration<'doughnut'>= {
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
                  text: `${lbl} (${(ds.data as number[])[i]})`,
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
              label: (ctx) => this.tooltipLabel(ctx as never),
            },
          },
          datalabels: {
            color: '#fff',
            font: { weight: 'bold' as const, size: 10 },
            formatter: (value: number) => {
              const pct = total > 0 ? ((value / total) * 100).toFixed(0) : '0';
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

  private buildBarChart(
    canvas: HTMLCanvasElement,
    dist: Record<string, number>,
    existing: Chart | null,
  ): Chart {
    existing?.destroy();

    const entries = Object.entries(dist)
      .map(([k, v]) => [k || 'Unknown', v] as [string, number])
      .sort((a, b) => b[1] - a[1])
      .slice(0, 15);
    const labels = entries.map(([k]) => k);
    const data = entries.map(([, v]) => v);
    const total = data.reduce((a, b) => a + b, 0);
    const colors = labels.map((_, i) => this.PALETTE[i % this.PALETTE.length]);

    const isDark = this.themeService.isDarkTheme();
    const textColor = isDark ? '#e8e8f0' : '#1a1a2e';
    const gridColor= isDark ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.06)';

    const config: ChartConfiguration<'bar'> = {
      type: 'bar',
      data: {
        labels,
        datasets: [
          {
            data,
            backgroundColor: colors,
            borderRadius: 6,
            borderSkipped: false,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 900, easing: 'easeInOutQuart' as const },
        indexAxis: 'y' as const,
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (ctx) => this.tooltipLabel(ctx as never),
            },
          },
          datalabels: {
            anchor: 'end',
            align: 'end',
            color: textColor,
            font: { size: 10, weight: 'bold' as const },
            formatter: (value: number) => {
              const pct = total > 0 ? ((value / total) * 100).toFixed(1) : '0';
              return `${pct}%`;
            },
          } as never,
        },
        scales: {
          x: { ticks: { color: textColor }, grid: { color: gridColor } },
          y: { ticks: { color: textColor, font: { size: 11 } }, grid: { display: false } },
        },
      },
    };

    return new Chart(canvas, config as unknown as ChartConfiguration);
  }

  // ── Shared helpers ─────────────────────────────────────────────────────────

  private tooltipLabel(ctx: { raw: unknown; dataset: { data: unknown[] } }): string {
    const values = ctx.dataset.data as number[];
    const total = values.reduce((a, b) => a + b, 0);
    const value = ctx.raw as number;
    const pct = total > 0 ? ((value / total) * 100).toFixed(1) : '0';
    return ` ${value} (${pct}%)`;
  }

  private rebuildAllCharts(): void {
    const vcCanvas = this.videoCodecCanvas();
    const vcDist = this.videoCodecDist();
    if (vcCanvas && Object.keys(vcDist).length > 0) {
      this.videoCodecChart = this.buildDoughnutChart(vcCanvas.nativeElement, vcDist, this.videoCodecChart);
    }

    const acCanvas = this.audioCodecCanvas();
    const acDist = this.audioCodecDist();
    if (acCanvas && Object.keys(acDist).length > 0) {
      this.audioCodecChart = this.buildDoughnutChart(acCanvas.nativeElement, acDist, this.audioCodecChart);
    }

    const resCanvas = this.resolutionCanvas();
    const resDist = this.resolutionDist();
    if (resCanvas && Object.keys(resDist).length > 0) {
      this.resolutionChart = this.buildBarChart(resCanvas.nativeElement, resDist, this.resolutionChart);
    }
  }
}

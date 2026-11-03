import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  ViewChild,
  AfterViewInit,
  OnDestroy,
  effect,
  inject,
  input,
  signal,
  DestroyRef,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import {
  Chart,
  ArcElement,
  BarElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  DoughnutController,
  BarController,
  ChartConfiguration,
} from 'chart.js';
import ChartDataLabels, { Context } from 'chartjs-plugin-datalabels';
import { ThemeService } from '../services/theme.service';
import { chartPalette, readToken } from '../shared/chart-theme';
import { UiCardComponent } from '../shared/ui/ui-card.component';
import { UiStatePanelComponent } from '../shared/ui/ui-state-panel/ui-state-panel.component';

Chart.register(
  ArcElement,
  BarElement,
  CategoryScale,
  LinearScale,
  Tooltip,
  Legend,
  DoughnutController,
  BarController,
  ChartDataLabels,
);

@Component({
  selector: 'app-genre-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, UiCardComponent, UiStatePanelComponent],
  templateUrl: './genre-chart.component.html',
  styleUrl: './genre-chart.component.scss',
})
export class GenreChartComponent implements AfterViewInit, OnDestroy {
  readonly genreStats = input<{ [key: string]: number }>({});
  @ViewChild('chartCanvas') canvasRef?: ElementRef<HTMLCanvasElement>;

  readonly themeService = inject(ThemeService);
  private translate = inject(TranslateService);
  private destroyRef = inject(DestroyRef);

  readonly chartType = signal<'doughnut' | 'bar'>('doughnut');
  private chart: Chart | null = null;
  private viewInit = signal(false);
  /** Pending deferred chart build (effect defers so @if can mount/unmount the canvas). */
  private buildTimer: ReturnType<typeof setTimeout> | null = null;

  /** Top-15 entries sorted descending — memoized so repeated reads never re-sort. */
  private readonly entries = computed<[string, number][]>(() =>
    Object.entries(this.genreStats())
      .sort((a, b) => b[1] - a[1])
      .slice(0, 15),
  );

  /** Top-15 entries sorted descending — also drives the empty state. */
  readonly hasData = computed(() => this.entries().length > 0);

  constructor() {
    // Rebuild on data, chart-type or theme change (colors are resolved from
    // semantic tokens at build time, so a theme flip needs a fresh build).
    effect(() => {
      const stats = this.genreStats();
      this.chartType();
      this.themeService.isDarkTheme();
      if (!this.viewInit()) return;
      // Capture for the deferred callback; @if can add/remove the canvas.
      const s = stats;
      this.scheduleBuild(s);
    });
  }

  /**
   * Defer a chart build to the next task so Angular can add/remove the
   * <canvas> via @if first. A pending build is replaced, never stacked.
   */
  private scheduleBuild(stats: { [key: string]: number }): void {
    if (this.buildTimer !== null) clearTimeout(this.buildTimer);
    this.buildTimer = setTimeout(() => {
      this.buildTimer = null;
      this.buildChart(stats);
    }, 0);
  }

  ngAfterViewInit(): void {
    this.viewInit.set(true);

    // Rebuild whenever the active language changes so labels are translated
    this.translate.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.scheduleBuild(this.genreStats()));
  }

  ngOnDestroy(): void {
    if (this.buildTimer !== null) clearTimeout(this.buildTimer);
    this.chart?.destroy();
  }

  setChartType(type: 'doughnut' | 'bar'): void {
    this.chartType.set(type);
  }

  /** Translate a MAL genre name using the i18n `genres.*` key, falling back to the raw name. */
  private translateGenre(raw: string): string {
    const key = `genres.${raw}`;
    const translated = this.translate.instant(key);
    return translated !== key ? translated : raw;
  }

  /** Chart.js tooltip label callback — shows count + percentage */
  private tooltipLabel(ctx: { raw: unknown; dataset: { data: unknown[] } }): string {
    const values = ctx.dataset.data as number[];
    const total = values.reduce((a, b) => a + b, 0);
    const value = ctx.raw as number;
    const pct = total > 0 ? ((value / total) * 100).toFixed(1) : '0';
    return ` ${value} (${pct}%)`;
  }

  private buildChart(stats: { [key: string]: number }): void {
    if (!this.viewInit()) return;

    this.chart?.destroy();
    this.chart = null;

    if (!this.canvasRef?.nativeElement) return;

    const rawEntries = Object.entries(stats)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 15);
    if (rawEntries.length === 0) return;

    const rawLabels = rawEntries.map(([k]) => k);
    const labels = rawLabels.map((k) => this.translateGenre(k));
    const data = rawEntries.map(([, v]) => v);
    const total = data.reduce((a, b) => a + b, 0);
    const colors = chartPalette(labels.length);

    // Theme-aware colors — semantic tokens resolved at build time.
    const textColor = readToken('--text-primary');
    const tickColor = readToken('--text-muted');
    const gridColor = readToken('--color-border-subtle');
    // Doughnut slice separators follow the card surface (the legacy #fff/#1a1a2e pair).
    const sliceBorder = readToken('--color-surface-elevated');
    const labelColor = readToken('--color-surface-elevated');

    const tooltipCallbacks = {
      label: (ctx: Parameters<typeof this.tooltipLabel>[0]) => this.tooltipLabel(ctx),
    };

    // Legend with count suffix: "Action (123)"
    const legendWithCount = {
      display: true,
      labels: {
        color: tickColor,
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
            fontColor: tickColor,
          }));
        },
      },
    };

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let config: ChartConfiguration;

    if (this.chartType() === 'doughnut') {
      const doughnutConfig: ChartConfiguration<'doughnut'> = {
        type: 'doughnut',
        data: {
          labels,
          datasets: [
            {
              data,
              backgroundColor: colors,
              borderWidth: 2,
              borderColor: sliceBorder,
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
            legend: { ...legendWithCount, position: 'right' },
            tooltip: { callbacks: tooltipCallbacks },
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            datalabels: {
              color: labelColor,
              font: { weight: 'bold' as const, size: 10 },
              formatter: (value: number) => {
                const pct = total > 0 ? ((value / total) * 100).toFixed(0) : '0';
                return `${pct}%`;
              },
              // Hide labels for very small slices to avoid clutter
              display: (ctx: Context) => {
                const pct = total > 0 ? (ctx.dataset.data[ctx.dataIndex] as number) / total : 0;
                return pct > 0.04;
              },
            } as never,
          },
        },
      };
      config = doughnutConfig as unknown as ChartConfiguration;
    } else {
      const barConfig: ChartConfiguration<'bar'> = {
        type: 'bar',
        data: {
          labels,
          datasets: [{ data, backgroundColor: colors, borderRadius: 6, borderSkipped: false }],
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          animation: { duration: 900, easing: 'easeInOutQuart' as const },
          indexAxis: 'y' as const,
          plugins: {
            legend: { display: false },
            tooltip: { callbacks: tooltipCallbacks },
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
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
            x: { ticks: { color: tickColor }, grid: { color: gridColor } },
            y: { ticks: { color: tickColor, font: { size: 11 } }, grid: { display: false } },
          },
        },
      };
      config = barConfig as unknown as ChartConfiguration;
    }

    this.chart = new Chart(this.canvasRef.nativeElement, config);
  }
}

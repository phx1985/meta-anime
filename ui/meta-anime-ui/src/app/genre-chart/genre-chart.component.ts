import {
  Component,
  Input,
  OnChanges,
  SimpleChanges,
  ElementRef,
  ViewChild,
  AfterViewInit,
  OnDestroy,
  inject,
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
  imports: [TranslatePipe],
  templateUrl: './genre-chart.component.html',
  styleUrl: './genre-chart.component.scss',
})
export class GenreChartComponent implements OnChanges, AfterViewInit, OnDestroy {
  @Input() genreStats: { [key: string]: number } = {};
  @ViewChild('chartCanvas') canvasRef!: ElementRef<HTMLCanvasElement>;

  themeService = inject(ThemeService);
  private translate = inject(TranslateService);
  private destroyRef = inject(DestroyRef);

  chartType: 'doughnut' | 'bar' = 'doughnut';
  private chart: Chart | null = null;
  private ready = false;

  readonly PALETTE = [
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

  ngAfterViewInit(): void {
    this.ready = true;
    this.buildChart();

    // Rebuild whenever the active language changes so labels are translated
    this.translate.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.buildChart());
  }

  ngOnChanges(_: SimpleChanges): void {
    if (this.ready) this.buildChart();
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }

  setChartType(type: 'doughnut' | 'bar'): void {
    this.chartType = type;
    this.buildChart();
  }

  private get entries(): [string, number][] {
    return Object.entries(this.genreStats)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 15);
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

  private buildChart(): void {
    if (!this.ready || !this.canvasRef) return;

    this.chart?.destroy();

    const rawEntries = this.entries;
    const rawLabels = rawEntries.map(([k]) => k);
    const labels = rawLabels.map((k) => this.translateGenre(k));
    const data = rawEntries.map(([, v]) => v);
    const total = data.reduce((a, b) => a + b, 0);
    const colors = labels.map((_, i) => this.PALETTE[i % this.PALETTE.length]);
    const isDark = this.themeService.isDarkTheme();
    const textColor = isDark ? '#e8e8f0' : '#1a1a2e';
    const gridColor = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.06)';

    const tooltipCallbacks = {
      label: (ctx: Parameters<typeof this.tooltipLabel>[0]) => this.tooltipLabel(ctx),
    };

    // Legend with count suffix: "Action (123)"
    const legendWithCount = {
      display: true,
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
    };

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let config: ChartConfiguration;

    if (this.chartType === 'doughnut') {
      const doughnutConfig: ChartConfiguration<'doughnut'> = {
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
            legend: { ...legendWithCount, position: 'right' },
            tooltip: { callbacks: tooltipCallbacks },
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            datalabels: {
              color: '#fff',
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
            x: { ticks: { color: textColor }, grid: { color: gridColor } },
            y: { ticks: { color: textColor, font: { size: 11 } }, grid: { display: false } },
          },
        },
      };
      config = barConfig as unknown as ChartConfiguration;
    }

    this.chart = new Chart(this.canvasRef.nativeElement, config);
  }
}

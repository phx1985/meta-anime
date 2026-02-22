import {
  Component, Input, OnChanges, SimpleChanges,
  ElementRef, ViewChild, AfterViewInit, OnDestroy, inject
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import {
  Chart, ArcElement, BarElement, CategoryScale, LinearScale,
  Tooltip, Legend, DoughnutController, BarController
} from 'chart.js';
import { ThemeService } from '../services/theme.service';

Chart.register(ArcElement, BarElement, CategoryScale, LinearScale, Tooltip, Legend, DoughnutController, BarController);

@Component({
  selector: 'app-genre-chart',
  standalone: true,
  imports: [TranslatePipe],
  templateUrl: './genre-chart.component.html',
  styleUrl: './genre-chart.component.scss'
})
export class GenreChartComponent implements OnChanges, AfterViewInit, OnDestroy {
  @Input() genreStats: { [key: string]: number } = {};
  @ViewChild('chartCanvas') canvasRef!: ElementRef<HTMLCanvasElement>;

  themeService = inject(ThemeService);

  chartType: 'doughnut' | 'bar' = 'doughnut';
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  private chart: any = null;
  private ready = false;

  readonly PALETTE = [
    '#6c63ff', '#3ecfcf', '#e5a00d', '#ef4444', '#22c55e',
    '#3b82f6', '#a855f7', '#ec4899', '#f97316', '#14b8a6',
    '#84cc16', '#06b6d4', '#f59e0b', '#8b5cf6', '#10b981'
  ];

  ngAfterViewInit(): void {
    this.ready = true;
    this.buildChart();
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

  private buildChart(): void {
    if (!this.ready || !this.canvasRef) return;

    this.chart?.destroy();

    const labels = this.entries.map(([k]) => k);
    const data   = this.entries.map(([, v]) => v);
    const colors = labels.map((_, i) => this.PALETTE[i % this.PALETTE.length]);
    const isDark = this.themeService.isDarkTheme();
    const textColor = isDark ? '#e8e8f0' : '#1a1a2e';
    const gridColor = isDark ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.06)';

    const commonOpts = {
      responsive: true,
      maintainAspectRatio: false,
      animation: { duration: 900, easing: 'easeInOutQuart' as const },
      plugins: {
        legend: { display: true, labels: { color: textColor, font: { size: 12 }, boxWidth: 14, padding: 12 } }
      }
    };

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let config: any;

    if (this.chartType === 'doughnut') {
      config = {
        type: 'doughnut',
        data: { labels, datasets: [{ data, backgroundColor: colors, borderWidth: 2, borderColor: isDark ? '#1a1a2e' : '#fff', hoverOffset: 12 }] },
        options: {
          ...commonOpts,
          cutout: '62%',
          plugins: {
            ...commonOpts.plugins,
            legend: { ...commonOpts.plugins.legend, position: 'right' }
          }
        }
      };
    } else {
      config = {
        type: 'bar',
        data: { labels, datasets: [{ data, backgroundColor: colors, borderRadius: 6, borderSkipped: false }] },
        options: {
          ...commonOpts,
          indexAxis: 'y',
          plugins: { ...commonOpts.plugins, legend: { display: false } },
          scales: {
            x: { ticks: { color: textColor }, grid: { color: gridColor } },
            y: { ticks: { color: textColor, font: { size: 11 } }, grid: { display: false } }
          }
        }
      };
    }

    this.chart = new Chart(this.canvasRef.nativeElement, config);
  }
}

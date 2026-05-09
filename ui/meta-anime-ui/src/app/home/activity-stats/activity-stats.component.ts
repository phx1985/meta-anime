import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  OnDestroy,
  OnInit,
  signal,
  ViewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import {
  Chart,
  CategoryScale,
  Filler,
  Legend,
  LinearScale,
  LineController,
  LineElement,
  PointElement,
  Tooltip,
} from 'chart.js';
import ChartDataLabels from 'chartjs-plugin-datalabels';
import { AnimeService } from '../../services/anime.service';
import { ProviderStateService } from '../../services/provider-state.service';
import { ActivityChartPoint, ActivityStatsResponse } from '../../models/anime.models';

// Register Chart.js elements needed for line charts (global, idempotent).
Chart.register(
  LineController,
  LineElement,
  PointElement,
  CategoryScale,
  LinearScale,
  Filler,
  Tooltip,
  Legend,
  ChartDataLabels,
);

type Period = 'today' | 'thisWeek' | 'thisMonth' | 'thisYear';

@Component({
  selector: 'app-activity-stats',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './activity-stats.component.html',
  styleUrl: './activity-stats.component.scss',
})
export class ActivityStatsComponent implements OnInit, AfterViewInit, OnDestroy {
  private animeService = inject(AnimeService);
  private destroyRef = inject(DestroyRef);
  readonly providerState = inject(ProviderStateService);

  @ViewChild('activityCanvas') canvasRef?: ElementRef<HTMLCanvasElement>;

  readonly activityStats = signal<ActivityStatsResponse | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly selectedPeriod = signal<Period>('thisMonth');
  private viewInit = signal(false);

  private chart: Chart | null = null;

  readonly periods: { key: Period; labelKey: string }[] = [
    { key: 'today',     labelKey: 'home.activityStats.today'     },
    { key: 'thisWeek',  labelKey: 'home.activityStats.thisWeek'  },
    { key: 'thisMonth', labelKey: 'home.activityStats.thisMonth' },
    { key: 'thisYear',  labelKey: 'home.activityStats.thisYear'  },
  ];

  readonly downloads = computed(() => {
    const stats = this.activityStats();
    if (!stats) return 0;
    return stats.downloads[this.selectedPeriod()];
  });

  readonly storageBytes = computed(() => {
    const stats = this.activityStats();
    if (!stats) return 0;
    return stats.storage[this.selectedPeriod()];
  });

  readonly storageFormatted = computed(() => this.formatBytes(this.storageBytes()));

  /** True when the activity stats card should be shown (requires Sonarr). */
  readonly isVisible = computed(() => this.providerState.sonarrEnabled());

  /** True when the chart should render (period has > 1 data point). */
  readonly showChart = computed(
    () => !this.loading() && !this.loadError() && !!this.activityStats() &&
          this.selectedPeriod() !== 'today',
  );

  constructor() {
    // React to signal changes; defer actual DOM update to next task so Angular
    // has time to add/remove the <canvas> element via @if (showChart()).
    effect(() => {
      const stats  = this.activityStats();
      const period = this.selectedPeriod();
      const init   = this.viewInit();
      if (!stats || !init) return;
      // Capture in local for the deferred callback (no stale closure on signals).
      const p = period;
      const s = stats;
      setTimeout(() => this.renderChart(s, p), 0);
    });
  }

  ngOnInit(): void {
    const tzOffset = new Date().getTimezoneOffset();
    this.animeService
      .getActivityStats(tzOffset)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.activityStats.set(data);
          this.loading.set(false);
        },
        error: () => {
          this.loadError.set(true);
          this.loading.set(false);
        },
      });
  }

  ngAfterViewInit(): void {
    this.viewInit.set(true);
  }

  ngOnDestroy(): void {
    this.destroyChart();
  }

  selectPeriod(period: Period): void {
    this.selectedPeriod.set(period);
  }

  private destroyChart(): void {
    this.chart?.destroy();
    this.chart = null;
  }

  private renderChart(stats: ActivityStatsResponse, period: Period): void {
    if (period === 'today' || !this.canvasRef?.nativeElement) {
      this.destroyChart();
      return;
    }

    const data: ActivityChartPoint[] =
      period === 'thisWeek'  ? stats.chart.week  :
      period === 'thisMonth' ? stats.chart.month :
      stats.chart.year;

    const labels    = data.map(p => p.label);
    const downloads = data.map(p => p.downloads);
    const dotRadius = data.length > 20 ? 2 : 4;

    if (this.chart) {
      this.chart.data.labels           = labels;
      this.chart.data.datasets[0].data = downloads;
      (this.chart.data.datasets[0] as any).pointRadius = dotRadius;
      this.chart.update('active');
      return;
    }

    this.chart = new Chart(this.canvasRef.nativeElement, {
      type: 'line',
      data: {
        labels,
        datasets: [
          {
            label: 'Downloads',
            data: downloads,
            borderColor: '#00aae0',
            backgroundColor: 'rgba(0,170,224,0.10)',
            fill: true,
            tension: 0.35,
            pointRadius: dotRadius,
            pointHoverRadius: 6,
            borderWidth: 2,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 350 },
        plugins: {
          legend: { display: false },
          datalabels: { display: false },
          tooltip: {
            mode: 'index',
            intersect: false,
            backgroundColor: 'rgba(20,20,30,0.9)',
            titleColor: 'rgba(180,180,200,0.8)',
            bodyColor: '#fff',
            borderColor: 'rgba(255,255,255,0.08)',
            borderWidth: 1,
            padding: 10,
          },
        },
        scales: {
          x: {
            grid: { display: false },
            border: { display: false },
            ticks: {
              color: 'rgba(150,150,170,0.65)',
              maxRotation: 0,
              maxTicksLimit: 15,
            },
          },
          y: {
            grid: { color: 'rgba(128,128,128,0.08)' },
            border: { display: false },
            beginAtZero: true,
            ticks: {
              color: 'rgba(150,150,170,0.65)',
              precision: 0,
            },
          },
        },
      },
    });
  }

  private formatBytes(bytes: number): string {
    if (bytes === 0) return '0 GB';
    const tb = bytes / 1_099_511_627_776;
    if (tb >= 1) return `${tb.toFixed(2)} TB`;
    const gb = bytes / 1_073_741_824;
    if (gb >= 1) return `${gb.toFixed(2)} GB`;
    const mb = bytes / 1_048_576;
    return `${mb.toFixed(0)} MB`;
  }
}


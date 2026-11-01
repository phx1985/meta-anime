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
  ViewChildren,
  QueryList,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
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
import { ThemeService } from '../../services/theme.service';
import { ActivityChartPoint, ActivityStatsResponse } from '../../models/anime.models';
import { readToken, withAlpha } from '../../shared/chart-theme';
import { UiCardComponent } from '../../shared/ui/ui-card.component';
import { UiStatePanelComponent } from '../../shared/ui/ui-state-panel/ui-state-panel.component';

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
  imports: [TranslatePipe, UiCardComponent, UiStatePanelComponent],
  templateUrl: './activity-stats.component.html',
  styleUrl: './activity-stats.component.scss',
})
export class ActivityStatsComponent implements OnInit, AfterViewInit, OnDestroy {
  private animeService = inject(AnimeService);
  private destroyRef = inject(DestroyRef);
  private translate = inject(TranslateService);
  readonly providerState = inject(ProviderStateService);
  readonly themeService = inject(ThemeService);

  @ViewChild('activityCanvas') canvasRef?: ElementRef<HTMLCanvasElement>;
  @ViewChildren('periodTab') tabButtons?: QueryList<ElementRef<HTMLButtonElement>>;

  readonly activityStats = signal<ActivityStatsResponse | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly refreshing = signal(false);
  readonly selectedPeriod = signal<Period>('thisMonth');
  private viewInit = signal(false);

  private chart: Chart | null = null;
  /** Theme the current chart instance was built for (forces rebuild on switch). */
  private chartTheme: boolean | null = null;
  /** Pending deferred chart render (effect defers so @if can mount/unmount the canvas). */
  private renderTimer: ReturnType<typeof setTimeout> | null = null;
  /** Pending poll tick of the refresh loop (cancellable on destroy). */
  private pollTimer: ReturnType<typeof setTimeout> | null = null;

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

  readonly lastUpdated = computed(() => {
    const raw = this.activityStats()?.lastUpdated;
    return raw ? this.formatLocalDate(raw) : null;
  });

  /** True when the activity stats card should be shown (requires Sonarr). */
  readonly isVisible = computed(() => this.providerState.sonarrEnabled());

  /** True when the chart should render (period has > 1 data point). */
  readonly showChart = computed(
    () => !this.loading() && !this.loadError() && !!this.activityStats() &&
          this.selectedPeriod() !== 'today',
  );

  constructor() {
    // React to signal changes (including theme — the chart is rebuilt with the
    // new token colors); defer actual DOM update to next task so Angular
    // has time to add/remove the <canvas> element via @if (showChart()).
    effect(() => {
      const stats  = this.activityStats();
      const period = this.selectedPeriod();
      const init   = this.viewInit();
      // Read for reactivity: a theme flip re-runs this effect so renderChart
      // rebuilds the chart with the freshly resolved token colors.
      this.themeService.isDarkTheme();
      if (!stats || !init) return;
      // Capture in local for the deferred callback (no stale closure on signals).
      const p = period;
      const s = stats;
      this.scheduleRender(s, p);
    });
  }

  /**
   * Defer a chart render to the next task so Angular can add/remove the
   * <canvas> via @if first. A pending render is replaced, never stacked,
   * so rapid signal flips cannot double-build the chart.
   */
  private scheduleRender(stats: ActivityStatsResponse, period: Period): void {
    if (this.renderTimer !== null) clearTimeout(this.renderTimer);
    this.renderTimer = setTimeout(() => {
      this.renderTimer = null;
      this.renderChart(stats, period);
    }, 0);
  }

  ngOnInit(): void {
    this.fetchStats();
  }

  ngAfterViewInit(): void {
    this.viewInit.set(true);

    // The dataset label is translated — rebuild the chart on language change.
    this.translate.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        const stats = this.activityStats();
        this.destroyChart();
        if (stats && this.showChart()) {
          this.scheduleRender(stats, this.selectedPeriod());
        }
      });
  }

  ngOnDestroy(): void {
    if (this.renderTimer !== null) clearTimeout(this.renderTimer);
    if (this.pollTimer !== null) clearTimeout(this.pollTimer);
    this.destroyChart();
  }

  selectPeriod(period: Period): void {
    this.selectedPeriod.set(period);
  }

  /**
   * Roving-tabindex keyboard support for the period tablist (ARIA tabs
   * pattern): arrows cycle, Home/End jump — selection follows focus.
   */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const count = this.periods.length;
    let next: number | null = null;
    switch (event.key) {
      case 'ArrowRight': next = (index + 1) % count; break;
      case 'ArrowLeft':  next = (index - 1 + count) % count; break;
      case 'Home':       next = 0; break;
      case 'End':        next = count - 1; break;
    }
    if (next === null) return;
    event.preventDefault();
    this.selectPeriod(this.periods[next].key);
    this.tabButtons?.get(next)?.nativeElement.focus();
  }

  /** Retry handler for the error state. */
  retryFetch(): void {
    this.fetchStats();
  }

  forceRefresh(): void {
    if (this.refreshing()) return;
    this.refreshing.set(true);
    // Capture the raw ISO string BEFORE the refresh so we can detect when it changes.
    const prevLastUpdated = this.activityStats()?.lastUpdated ?? null;

    this.animeService
      .refreshActivityStats()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.pollUntilUpdated(prevLastUpdated, 0),
        error: () => this.refreshing.set(false),
      });
  }

  /** Polls getActivityStats every second until lastUpdated changes (max 30 attempts). */
  private pollUntilUpdated(prevLastUpdated: string | null, attempt: number): void {
    if (attempt >= 30) {
      this.refreshing.set(false);
      return;
    }
    // Tracked so the whole 30×1s poll chain is cancelled on destroy.
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      this.animeService
        .getActivityStats(new Date().getTimezoneOffset())
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (data) => {
            if (data.lastUpdated !== prevLastUpdated) {
              this.activityStats.set(data);
              this.loading.set(false);
              this.refreshing.set(false);
            } else {
              this.pollUntilUpdated(prevLastUpdated, attempt + 1);
            }
          },
          error: () => this.refreshing.set(false),
        });
    }, 1000);
  }

  private fetchStats(isRefresh = false): void {
    if (!isRefresh) {
      this.loading.set(true);
      this.loadError.set(false);
    }
    const tzOffset = new Date().getTimezoneOffset();
    this.animeService
      .getActivityStats(tzOffset)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.activityStats.set(data);
          this.loading.set(false);
          this.refreshing.set(false);
        },
        error: () => {
          this.loadError.set(true);
          this.loading.set(false);
          this.refreshing.set(false);
        },
      });
  }

  private destroyChart(): void {
    this.chart?.destroy();
    this.chart = null;
    this.chartTheme = null;
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

    // Theme-aware colors — resolved from semantic tokens at build time so
    // theme switches stay in sync (the chart is rebuilt when the theme flips).
    const theme = this.themeService.isDarkTheme();
    const seriesColor = readToken('--color-brand');
    const surfaceColor = readToken('--color-surface-elevated');
    const tickColor = readToken('--text-muted');
    const gridColor = readToken('--color-border-subtle');
    const tooltipBg = withAlpha(readToken('--text-primary'), 0.92);
    const tooltipTitle = withAlpha(readToken('--text-secondary'), 0.8);
    const tooltipBorder = readToken('--color-border-subtle');

    if (this.chart && this.chartTheme === theme) {
      this.chart.data.labels           = labels;
      this.chart.data.datasets[0].data = downloads;
      (this.chart.data.datasets[0] as any).pointRadius = dotRadius;
      this.chart.update('active');
      return;
    }

    // Theme changed (or first build): rebuild from scratch with fresh tokens.
    this.destroyChart();
    this.chartTheme = theme;

    this.chart = new Chart(this.canvasRef.nativeElement, {
      type: 'line',
      data: {
        labels,
        datasets: [
          {
            label: this.translate.instant('home.activityStats.downloads'),
            data: downloads,
            borderColor: seriesColor,
            backgroundColor: withAlpha(seriesColor, 0.1),
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
          // datalabels is registered globally — explicitly opt out on this chart
          datalabels: { display: false },
          tooltip: {
            mode: 'index',
            intersect: false,
            backgroundColor: tooltipBg,
            titleColor: tooltipTitle,
            bodyColor: surfaceColor,
            borderColor: tooltipBorder,
            borderWidth: 1,
            padding: 10,
          },
        },
        scales: {
          x: {
            grid: { display: false },
            border: { display: false },
            ticks: {
              color: tickColor,
              maxRotation: 0,
              maxTicksLimit: 15,
            },
          },
          y: {
            grid: { color: gridColor },
            border: { display: false },
            beginAtZero: true,
            ticks: {
              color: tickColor,
              precision: 0,
            },
          },
        },
      },
    });
  }

  private formatLocalDate(isoUtc: string): string {
    const d = new Date(isoUtc);
    if (isNaN(d.getTime())) return isoUtc;
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
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

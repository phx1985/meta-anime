import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';
import { AnimeService } from '../services/anime.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { AnimeDetailPanelComponent } from '../anime-detail-panel/anime-detail-panel.component';
import { ProviderIconComponent, ProviderIconType } from '../shared/components/provider-icon/provider-icon.component';
import { ServerLogsComponent } from './server-logs/server-logs.component';
import {
  ActivityConfigResponse,
  ActivityFeedItem,
  ActivityStatsResponse,
  ActivitySummaryResponse,
  ActivityTrendPoint,
  ActivityTrendResponse,
} from '../models/anime.models';

type FeedFilter = 'all' | 'adds' | 'episodes' | 'watched' | 'completions' | 'syncs';
type StatsPeriod = 'today' | 'thisWeek' | 'thisMonth' | 'thisYear';
/** Left column content: the media timeline or the API's own log stream. */
type HubView = 'timeline' | 'logs';

/** One row of an expanded sync payload. */
interface SyncDiffRow {
  provider: string;
  action: string;
  title: string;
  reason: string;
}

/** A plotted point of the trend sparkline, in the 720×200 viewBox. */
/**
 * Activity & Logs hub: a left-hand media timeline with provider deep links and a right-hand
 * analytics sidebar (KPI bento, 7-day trend, last metadata sync). Live via SignalR — no polling
 * dropdown, no export buttons, no page reloads.
 */
@Component({
  selector: 'app-activity-feed',
  standalone: true,
  imports: [FormsModule, TranslatePipe, AnimeDetailPanelComponent, ProviderIconComponent, ServerLogsComponent],
  templateUrl: './activity-feed.component.html',
  styleUrl: './activity-feed.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivityFeedComponent {
  private readonly animeService = inject(AnimeService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly sync = inject(SyncSignalRService);
  private readonly translate = inject(TranslateService);

  private static readonly PAGE_SIZE = 25;
  /** Safety net only: SignalR triggers the real refreshes. */
  private static readonly REFRESH_MS = 60000;
  private static readonly VIEWBOX = { width: 720, height: 200, padLeft: 36, padRight: 20, padTop: 16, padBottom: 28 };

  /** Timeline vs. the API log stream (server logs now live here, not in Settings). */
  readonly view = signal<HubView>('timeline');
  /** Seconds the activity backfill has been running — the only progress a cold start can show. */
  readonly backfillSeconds = signal(0);

  readonly filter = signal<FeedFilter>('all');
  readonly search = signal('');
  readonly items = signal<ActivityFeedItem[]>([]);
  readonly stats = signal<ActivityStatsResponse | null>(null);
  readonly trend = signal<ActivityTrendResponse | null>(null);
  /** Explicit year for the "Year" range (null = current year). */
  readonly year = signal<number | null>(null);
  readonly years = signal<number[]>([]);
  readonly summary = signal<ActivitySummaryResponse | null>(null);
  readonly counts = signal<Record<string, number>>({});
  readonly loading = signal(true);
  readonly refreshing = signal(false);
  readonly error = signal(false);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly expanded = signal<ReadonlySet<string>>(new Set());
  readonly period = signal<StatsPeriod>('thisMonth');

  /** In-place detail drawer — /activity never navigates away. */
  readonly detailMalId = signal<string | null>(null);
  readonly detailAnimeId = signal<string | null>(null);

  /** Live SignalR connection state for the header badge. */
  readonly liveConnected = signal(false);

  /** True while a filter switch is settling — drives the 120 ms crossfade, never a blank pane. */
  readonly switching = signal(false);

  /** Index of the trend point under the cursor (single hover dot; no static baseline circles). */
  readonly hoverIndex = signal<number | null>(null);

  readonly hasMore = computed(() => this.items().length < this.total());

  readonly filters: Array<{ key: FeedFilter; labelKey: string }> = [
    { key: 'all', labelKey: 'activity.hub.filter.all' },
    { key: 'adds', labelKey: 'activity.hub.filter.adds' },
    { key: 'episodes', labelKey: 'activity.hub.filter.episodes' },
    { key: 'watched', labelKey: 'activity.hub.filter.watched' },
    { key: 'completions', labelKey: 'activity.hub.filter.completions' },
    { key: 'syncs', labelKey: 'activity.hub.filter.syncs' },
  ];

  /** Label of the selected range, shown next to the chart title once the pills moved to the header. */
  readonly periodLabelKey = computed(() => {
    switch (this.period()) {
      case 'today': return 'activity.range.today';
      case 'thisWeek': return 'activity.range.week';
      case 'thisYear': return 'activity.range.year';
      default: return 'activity.range.month';
    }
  });

  readonly periods: Array<{ key: StatsPeriod; labelKey: string }> = [
    { key: 'today', labelKey: 'activity.range.today' },
    { key: 'thisWeek', labelKey: 'activity.range.week' },
    { key: 'thisMonth', labelKey: 'activity.range.month' },
    { key: 'thisYear', labelKey: 'activity.range.year' },
  ];

  /** Raw buckets of the multi-series trend for the selected range. */
  readonly trendPoints = computed<ActivityTrendPoint[]>(() => this.trend()?.points ?? []);

  /** Integer Y-axis maximum across ALL series, always a multiple of 4. */
  readonly tickMax = computed(() => {
    const rawMax = this.trendPoints().reduce(
      (max, p) => Math.max(max, p.ingested, p.added, p.watched, p.syncs), 0);
    return Math.max(4, Math.ceil(rawMax / 4) * 4);
  });

  /** Five unique integer ticks, top to bottom — never the 0/1/1 duplication. */
  readonly yAxisTicks = computed(() => {
    const max = this.tickMax();
    return [max, (max * 3) / 4, max / 2, max / 4, 0].map((t) => Math.round(t));
  });

  /** True when the selected period has no activity at all. */
  readonly emptyPeriod = computed(() => this.trendPoints().every(
    (p) => p.ingested + p.added + p.watched + p.syncs === 0));

  /** One line per metric, same buckets, same scale. */
  readonly series = computed(() => {
    const points = this.trendPoints();
    if (points.length < 2) return [];

    const defs = [
      { key: 'ingested' as const, labelKey: 'activity.kpi.episodesIngested', color: '#38bdf8' },
      { key: 'watched' as const, labelKey: 'activity.kpi.episodesWatched', color: '#fbbf24' },
      { key: 'added' as const, labelKey: 'activity.kpi.netAnimeAdded', color: '#4ade80' },
      { key: 'syncs' as const, labelKey: 'activity.hub.filter.syncs', color: '#a78bfa' },
    ];

    return defs.map((def) => ({
      ...def,
      path: this.buildPath(points.map((p) => p[def.key])),
    }));
  });

  /** "Mon · 12 ingested · 3 watched" style tooltip for the hovered bucket. */
  readonly hoverLabel = computed(() => {
    const index = this.hoverIndex();
    const points = this.trendPoints();
    if (index === null || index < 0 || index >= points.length) return '';

    const p = points[index];
    return `${p.label} · ${p.ingested + p.added + p.watched + p.syncs}`;
  });

  readonly hoverX = computed(() => {
    const index = this.hoverIndex();
    const points = this.trendPoints();
    if (index === null || index < 0 || index >= points.length) return null;
    const { padLeft, padRight, width } = ActivityFeedComponent.VIEWBOX;
    const innerW = width - padLeft - padRight;
    return padLeft + (innerW / Math.max(1, points.length - 1)) * index;
  });

  /** True while the first-run prompt waits for its POST to come back. */
  readonly startingBackfill = signal(false);

  /** No completed backfill yet — the hub has nothing to show until one runs. */
  readonly neverImported = computed(() => this.config() !== null && this.config()!.lastRunAt == null);

  /** Whether a Plex server is wired up — the "Watched on Plex" tile is dead without one. */
  private readonly config = signal<ActivityConfigResponse | null>(null);
  readonly plexConfigured = computed(() => this.config()?.plexConfigured ?? true);

  /** Storage for the SELECTED range (the tile used to be hard-wired to `thisMonth`). */
  readonly storageForPeriod = computed(() => {
    const storage = this.stats()?.storage;
    if (!storage) return 0;
    switch (this.period()) {
      case 'today': return storage.today;
      case 'thisWeek': return storage.thisWeek;
      case 'thisYear': return storage.thisYear;
      default: return storage.thisMonth;
    }
  });

  /** Last metadata-sync event in the current feed payload (drives the sidebar summary card). */
  readonly lastSync = computed<ActivityFeedItem | null>(
    () => this.items().find((item) => item.type === 'SyncCompleted') ?? null,
  );

  /** True when a live activity job is running (spinning refresh + veil). */
  readonly isBusy = computed(() => this.refreshing() || this.sync.isActivitySyncRunning());

  private readonly timer: ReturnType<typeof setInterval>;
  private searchTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const v = params.get('view');
      this.view.set(v === 'logs' ? 'logs' : 'timeline');
      const f = (params.get('filter') ?? 'all') as FeedFilter;
      this.filter.set(this.filters.some((x) => x.key === f) ? f : 'all');
      this.search.set(params.get('search') ?? '');
      this.page.set(1);
      this.reloadAll();
    });

    // A sync finishing anywhere in the app must refresh this page. No F5.
    this.sync.syncDone$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((done) => {
      if (done) this.reloadAll(true);
    });

    // The activity backfill runs in a Hangfire job: without this the first rows only show up
    // on the next 60 s tick. Reload the moment the job reports completion.
    this.sync.activitySyncDone$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.backfillSeconds.set(0);
      this.reloadAll(true);
    });

    this.sync.connected$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((connected) => this.liveConnected.set(connected));

    this.timer = setInterval(() => {
      if (document.visibilityState === 'visible') this.load(true, true);
    }, ActivityFeedComponent.REFRESH_MS);
    this.destroyRef.onDestroy(() => clearInterval(this.timer));

    // 1 s heartbeat for the backfill card only — cheap, and it stops the "is it frozen?"
    // feeling while a cold import runs with an empty feed.
    const backfillTick = setInterval(() => {
      if (this.isBusy()) this.backfillSeconds.update((s) => s + 1);
      else if (this.backfillSeconds() !== 0) this.backfillSeconds.set(0);
    }, 1000);
    this.destroyRef.onDestroy(() => clearInterval(backfillTick));
  }

  reloadAll(silent = false): void {
    this.load(true, silent);
    this.loadStats();
    this.loadTrend();
    this.loadSummary();
    this.loadCounts();
    this.loadConfig();
    if (this.years().length === 0) this.loadYears();
  }

  /**
   * Filter changes keep the previous rows on screen until the new page arrives (the container
   * never collapses into a grey pane), with a short opacity crossfade instead of a flash.
   */
  setView(view: HubView): void {
    if (this.view() === view) return;
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { view: view === 'logs' ? 'logs' : null, filter: view === 'logs' ? null : this.filter() },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  setFilter(filter: FeedFilter): void {
    if (this.filter() === filter) return;
    this.switching.set(true);
    setTimeout(() => this.switching.set(false), 160);
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { filter, search: this.search() || null },
      replaceUrl: true,
    });
  }

  setPeriod(period: StatsPeriod): void {
    if (this.period() === period) return;
    this.period.set(period);
    this.hoverIndex.set(null);
    this.page.set(1);
    this.load(true);
    this.loadTrend();
    this.loadSummary();
    this.loadCounts();
  }

  onSearch(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { filter: this.filter(), search: value.trim() || null },
        replaceUrl: true,
      });
    }, 300);
  }

  clearSearch(): void {
    clearTimeout(this.searchTimer);
    this.onSearch('');
  }

  load(reset = false, silent = false): void {
    if (!silent) this.loading.set(true);
    if (reset) this.page.set(1);

    this.animeService
      .getActivityFeed(
        this.filter(),
        this.page(),
        ActivityFeedComponent.PAGE_SIZE,
        this.search(),
        this.period(),
        new Date().getTimezoneOffset(),
        this.year(),
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.total.set(res.total);
          this.items.set(reset ? res.items : [...this.items(), ...res.items]);
          this.loading.set(false);
          this.error.set(false);
        },
        error: () => {
          this.loading.set(false);
          if (!silent) this.error.set(true);
        },
      });
  }

  /** Years available in the mirror — the picker only shows when "Year" is selected. */
  loadYears(): void {
    this.animeService
      .getActivityYears()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (years) => {
          this.years.set(years);
          if (!this.year() && years.length > 0) this.year.set(years[0]);
        },
        error: () => {},
      });
  }

  setYear(year: number): void {
    if (this.year() === year) return;
    this.year.set(year);
    this.page.set(1);
    this.load(true);
    this.loadTrend();
    this.loadSummary();
    this.loadCounts();
  }

  /** Multi-series trend for the chart — same period as the tiles. */
  loadTrend(): void {
    this.animeService
      .getActivityTrend(this.period(), new Date().getTimezoneOffset(), this.year())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (t) => this.trend.set(t), error: () => {} });
  }

  loadStats(): void {
    this.animeService
      .getActivityStats(new Date().getTimezoneOffset())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (s) => this.stats.set(s), error: () => {} });
  }

  private loadConfig(): void {
    this.animeService
      .getActivityConfig()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (c) => this.config.set(c), error: () => {} });
  }

  /** First run: the user decides when the (potentially slow) history import starts. */
  startBackfill(): void {
    if (this.startingBackfill()) return;
    this.startingBackfill.set(true);
    this.animeService.refreshActivity().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.startingBackfill.set(false);
        this.backfillSeconds.set(0);
        // The job broadcasts activitySyncStarted, but poll once as well so a dropped
        // SignalR frame cannot leave the page stuck on the prompt.
        setTimeout(() => this.reloadAll(true), 1200);
      },
      error: () => this.startingBackfill.set(false),
    });
  }

  /** The range pills are global: the tiles ask for the same calendar window as the chart. */
  loadSummary(): void {
    this.animeService
      .getActivityPeriodSummary(this.period(), new Date().getTimezoneOffset(), this.year())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (s) => this.summary.set(s), error: () => {} });
  }

  /** Chip badges: one cheap pageSize=1 call per stream. */
  loadCounts(): void {
    const streams: FeedFilter[] = ['adds', 'episodes', 'watched', 'completions', 'syncs'];
    forkJoin(streams.map((s) => this.animeService.getActivityFeed(
      s, 1, 1, '', this.period(), new Date().getTimezoneOffset(), this.year())))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          const next: Record<string, number> = {};
          streams.forEach((s, i) => (next[s] = res[i].total));
          this.counts.set(next);
        },
        error: () => {},
      });
  }

  /**
   * Human-readable date + time for the tooltip, in the BROWSER's timezone. The raw ISO string
   * used to be shown there, which reads as UTC and is useless at a glance.
   */
  formatTimestamp(iso: string): string {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return iso;

    const locale = this.translate.currentLang() === 'fr' ? 'fr-FR' : 'en-US';
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
  }

  /** "12m ago" / "2h ago" with the exact timestamp in the title attribute. */
  relativeTime(iso: string): string {
    const then = new Date(iso).getTime();
    if (Number.isNaN(then)) return '';
    const minutes = Math.max(0, Math.round((Date.now() - then) / 60000));
    if (minutes < 1) return 'now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.round(hours / 24);
    return days === 1 ? 'yesterday' : `${days}d ago`;
  }

  providerClass(provider: string): string {
    switch (provider) {
      case 'Sonarr': return 'chip--sonarr';
      case 'Plex': return 'chip--plex';
      default: return 'chip--mal';
    }
  }

  /** Episode code (S03E05) pulled out of the subtitle so it can render as its own pill. */
  episodeCode(item: ActivityFeedItem): string | null {
    const match = /S\d{2}E\d{2}(?:[–-]E\d{2})?/.exec(item.subtitle ?? '');
    return match ? match[0] : null;
  }

  /** Remaining subtitle text once the episode code is stripped. */
  restOfSubtitle(item: ActivityFeedItem): string {
    const code = this.episodeCode(item);
    if (!code) return item.subtitle ?? '';
    return (item.subtitle ?? '').replace(code, '').replace(/^[·\s—–-]+/, '').trim();
  }

  /** Event pill label ("+ ANIME ADDED", "▶ WATCHED ON PLEX", …). */
  eventKey(item: ActivityFeedItem): string {
    switch (item.type) {
      case 'AnimeAdded': return 'activity.event.added';
      case 'AnimeRemoved': return 'activity.event.removed';
      case 'EpisodeDownloaded': return 'activity.event.ingested';
      case 'EpisodeWatched': return 'activity.event.watched';
      case 'SeasonCompleted': return 'activity.event.completed';
      default: return 'activity.event.sync';
    }
  }

  isSync(item: ActivityFeedItem): boolean {
    return item.type === 'SyncCompleted';
  }

  providerIcon(provider: string): ProviderIconType {
    switch (provider) {
      case 'sonarr': return 'sonarr';
      case 'plex': return 'plex';
      case 'kometa': return 'kometa';
      default: return 'mal';
    }
  }

  /** Numeric field from the payload (sonarrTotal, malTotal, plexTotal, durationMs…). */
  payloadNumber(item: ActivityFeedItem, key: string): number | null {
    if (!item.metadataJson) return null;
    try {
      const value = (JSON.parse(item.metadataJson) as Record<string, unknown>)[key];
      return typeof value === 'number' ? value : null;
    } catch {
      return null;
    }
  }

  providerLinks(item: ActivityFeedItem) {
    return item.providerLinks ?? [];
  }

  forceRefresh(): void {
    this.refreshing.set(true);
    this.animeService
      .refreshActivityStats()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => setTimeout(() => { this.refreshing.set(false); this.reloadAll(true); }, 900),
        error: () => this.refreshing.set(false),
      });
  }

  loadMore(): void {
    if (!this.hasMore() || this.loading()) return;
    this.page.update((p) => p + 1);
    this.load(false);
  }

  toggleExpand(item: ActivityFeedItem): void {
    const next = new Set(this.expanded());
    next.has(item.id) ? next.delete(item.id) : next.add(item.id);
    this.expanded.set(next);
  }

  isExpanded(item: ActivityFeedItem): boolean {
    return this.expanded().has(item.id);
  }

  /** Itemized diff rows stored in the sync payload. */
  diffs(item: ActivityFeedItem): SyncDiffRow[] {
    if (!item.metadataJson) return [];
    try {
      const meta = JSON.parse(item.metadataJson) as { diffs?: SyncDiffRow[]; isBaseline?: boolean };
      if (meta.isBaseline) return [];
      return (meta.diffs ?? []).slice(0, 40);
    } catch {
      return [];
    }
  }

  isBaseline(item: ActivityFeedItem): boolean {
    if (!item.metadataJson) return false;
    try {
      return (JSON.parse(item.metadataJson) as { isBaseline?: boolean }).isBaseline === true;
    } catch {
      return false;
    }
  }

  hasDetails(item: ActivityFeedItem): boolean {
    return this.diffs(item).length > 0 || this.isBaseline(item);
  }

  openDetails(item: ActivityFeedItem): void {
    // The internal id is the only one guaranteed to exist in the local DB — a resolved MAL id
    // can point at an entry that was never synced, which opened an empty drawer. Prefer it.
    if (item.animeId) {
      this.detailMalId.set(null);
      this.detailAnimeId.set(item.animeId);
      return;
    }

    if (item.malId) {
      this.detailAnimeId.set(null);
      this.detailMalId.set(String(item.malId));
    }
  }

  closeDetails(): void {
    this.detailMalId.set(null);
    this.detailAnimeId.set(null);
  }

  /** Full class list: a `[class]` binding must carry the base class too, or the card loses its layout. */
  severityClass(item: ActivityFeedItem): string {
    const base = item.isReverted ? 'warning' : item.severity ?? 'info';
    return `timeline-card timeline-card--${base}`;
  }

  icon(type: string): string {
    switch (type) {
      case 'AnimeAdded': return '＋';
      case 'AnimeRemoved': return '−';
      case 'EpisodeDownloaded': return '↓';
      case 'EpisodeWatched': return '▶';
      case 'SeasonCompleted': return '✓';
      default: return '⟳';
    }
  }

  /** 0 renders as "0 B", never "—". */
  formatSize(bytes: number | null | undefined): string {
    const value = bytes ?? 0;
    if (value <= 0) return '0 B';
    const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
    let scaled = value;
    let unit = 0;
    while (scaled >= 1024 && unit < units.length - 1) {
      scaled /= 1024;
      unit++;
    }
    return `${scaled.toFixed(unit === 0 ? 0 : 2)} ${units[unit]}`;
  }

  /** Seconds with one decimal, from the sync payload's `durationMs`. */
  formatDuration(ms: number | null): string {
    if (ms === null) return '—';
    return `${(ms / 1000).toFixed(1)} s`;
  }

  /** Hover crosshair: maps the pointer onto the nearest bucket. */
  onChartPointer(event: PointerEvent): void {
    const points = this.trendPoints();
    if (points.length === 0) return;
    const target = event.currentTarget as SVGSVGElement;
    const rect = target.getBoundingClientRect();
    const { padLeft, padRight, width } = ActivityFeedComponent.VIEWBOX;
    const ratio = (event.clientX - rect.left) / Math.max(1, rect.width);
    const x = ratio * width;
    const inner = width - padLeft - padRight;
    const index = Math.round(((x - padLeft) / inner) * (points.length - 1));
    this.hoverIndex.set(Math.min(points.length - 1, Math.max(0, index)));
  }

  clearHover(): void {
    this.hoverIndex.set(null);
  }

  /** Smooth cubic path for one series over the padded 720×200 viewBox. */
  private buildPath(values: number[]): string {
    if (values.length < 2) return '';

    const { width, height, padLeft, padRight, padTop, padBottom } = ActivityFeedComponent.VIEWBOX;
    const max = this.tickMax();
    const innerW = width - padLeft - padRight;
    const usableH = height - padTop - padBottom;

    const points = values.map((value, i) => ({
      x: padLeft + (innerW / Math.max(1, values.length - 1)) * i,
      y: padTop + usableH - (value / max) * usableH,
    }));

    let line = `M ${points[0].x.toFixed(2)} ${points[0].y.toFixed(2)}`;
    for (let i = 1; i < points.length; i++) {
      const prev = points[i - 1];
      const current = points[i];
      const cx = (prev.x + current.x) / 2;
      line += ` C ${cx.toFixed(2)} ${prev.y.toFixed(2)}, ${cx.toFixed(2)} ${current.y.toFixed(2)}, ${current.x.toFixed(2)} ${current.y.toFixed(2)}`;
    }

    return line;
  }
}

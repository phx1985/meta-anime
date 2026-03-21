import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  HostListener,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslatePipe } from '@ngx-translate/core';
import { LibraryAnalysisService } from '../services/library-analysis.service';
import { ProviderStateService } from '../services/provider-state.service';
import { LibraryAnalysisSummary, LibrarySeriesRow } from '../models/library-analysis.models';
import { TelemetryPillsComponent } from './telemetry-pills/telemetry-pills.component';
import { AnalyticsChartsComponent } from './analytics-charts/analytics-charts.component';
import { AnalysisTableComponent } from './analysis-table/analysis-table.component';
import { DetailPanelComponent } from './detail-panel/detail-panel.component';
import { AnimeDetailPanelComponent } from '../anime-detail-panel/anime-detail-panel.component';

@Component({
  selector: 'app-library-analysis',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TranslatePipe,
    TelemetryPillsComponent,
    AnalyticsChartsComponent,
    AnalysisTableComponent,
    DetailPanelComponent,
    AnimeDetailPanelComponent,
  ],
  templateUrl: './library-analysis.component.html',
  styleUrl: './library-analysis.component.scss',
})
export class LibraryAnalysisComponent {
  private readonly analysisService = inject(LibraryAnalysisService);
  private readonly providerState = inject(ProviderStateService);
  private readonly destroyRef = inject(DestroyRef);

  // ── Desktop detection ────────────────────────────────────────────────────
  readonly isDesktop = signal(typeof window !== 'undefined' ? window.innerWidth >= 1024 : true);

  // ── Provider state ───────────────────────────────────────────────────────
  readonly sonarrEnabled = this.providerState.sonarrEnabled;

  // ── Loading states ───────────────────────────────────────────────────────
  readonly loadingSummary = signal(true);
  readonly enriching = signal(false);
  readonly error = signal<string | null>(null);

  // ── Data ─────────────────────────────────────────────────────────────────
  readonly summary = signal<LibraryAnalysisSummary | null>(null);
  readonly enrichedRows = signal<LibrarySeriesRow[]>([]);

  // ── Detail panel ─────────────────────────────────────────────────────────
  readonly selectedRow = signal<LibrarySeriesRow | null>(null);
  readonly selectedAnimeId = signal<string | null>(null);

  // ── Table reference (for preset navigation from pill clicks) ─────────────
  private readonly tableRef = viewChild(AnalysisTableComponent);

  // ── Merged rows: start with summary rows, overlay enriched data ─────────
  readonly displayRows = computed(() => {
    const base = this.summary()?.series ?? [];
    const enriched = this.enrichedRows();
    if (!enriched.length) return base;

    const enrichedMap = new Map(enriched.map(r => [r.sonarrId, r]));
    return base.map(row => {
      const e = enrichedMap.get(row.sonarrId);
      if (!e) return row;
      // Merge: keep Phase 1 metadata, overlay Phase 2 codec/file enrichment
      return {
        ...row,
        primaryVideoCodec: e.primaryVideoCodec,
        primaryAudioCodec: e.primaryAudioCodec,
        primaryResolution: e.primaryResolution,
        primaryBitDepth: e.primaryBitDepth,
        videoCodecDistribution: e.videoCodecDistribution,
        audioCodecDistribution: e.audioCodecDistribution,
        resolutionDistribution: e.resolutionDistribution,
        episodeFiles: e.episodeFiles,
        fileCount: e.fileCount || row.fileCount,
        totalSizeBytes: e.totalSizeBytes || row.totalSizeBytes,
        avgEpisodeSizeBytes: e.avgEpisodeSizeBytes || row.avgEpisodeSizeBytes,
      };
    });
  });

  // ── Aggregated distributions for charts ─────────────────────────────────
  readonly videoCodecDist = computed(() => this.aggregateDistribution('videoCodecDistribution'));
  readonly audioCodecDist = computed(() => this.aggregateDistribution('audioCodecDistribution'));
  readonly resolutionDist = computed(() => this.aggregateDistribution('resolutionDistribution'));

  // ── Avg episode size threshold (from summary, default 1200 MB) ───────────
  readonly avgEpSizeThresholdBytes = computed(() =>
    (this.summary()?.avgEpSizeThresholdMb ?? 1200) * 1024 * 1024,
  );

  // ── Top 5 storage hogs ──────────────────────────────────────────────────
  readonly topStorageHogs = computed(() => {
    const rows = this.displayRows();
    return [...rows]
      .sort((a, b) => b.totalSizeBytes - a.totalSizeBytes)
      .slice(0, 5)
      .map(r => ({ title: r.title, totalSizeBytes: r.totalSizeBytes }));
  });

  @HostListener('window:resize')
  onResize(): void {
    this.isDesktop.set(window.innerWidth >= 1024);
  }

  constructor() {
    this.loadData();
  }

  loadData(): void {
    this.loadingSummary.set(true);
    this.enriching.set(false);
    this.error.set(null);
    this.enrichedRows.set([]);
    this.selectedRow.set(null);

    // Phase 1: Get summary
    this.analysisService.getSummary()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.summary.set(data);
          this.loadingSummary.set(false);

          // Phase 2: Enrich with codec/resolution data
          if (data.series.length > 0) {
            this.enriching.set(true);
            const ids = data.series.map(s => s.sonarrId);
            this.analysisService.enrichSeries(ids)
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe({
                next: (enriched) => {
                  this.enrichedRows.set(enriched);
                  this.enriching.set(false);
                },
                error: (err) => {
                  console.error('Enrichment failed:', err);
                  this.enriching.set(false);
                  // Non-fatal: table still works with Phase 1 data
                },
              });
          }
        },
        error: (err) => {
          console.error('Summary failed:', err);
          this.loadingSummary.set(false);
          this.error.set(err?.error ?? err?.message ?? 'Unknown error');
        },
      });
  }

  onRowClicked(row: LibrarySeriesRow): void {
    this.selectedRow.set(row);
  }

  onMissingFilesClick(): void {
    this.tableRef()?.applyPreset({ hasFiles: 'no', sortCol: 'title', sortDir: 'asc' });
  }

  onDetailClosed(): void {
    this.selectedRow.set(null);
  }

  onRequestAnimeDetail(id: string): void {
    this.selectedAnimeId.set(id);
  }

  private aggregateDistribution(
    field: 'videoCodecDistribution' | 'audioCodecDistribution' | 'resolutionDistribution',
  ): Record<string, number> {
    const rows = this.enrichedRows();
    const agg: Record<string, number> = {};
    for (const row of rows) {
      const dist = row[field];
      if (dist) {
        for (const [key, count] of Object.entries(dist)) {
          agg[key] = (agg[key] ?? 0) + count;
        }
      }
    }
    return agg;
  }
}

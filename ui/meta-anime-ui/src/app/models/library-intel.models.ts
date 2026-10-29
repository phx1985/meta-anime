import { LibraryEpisodeFileInfo, LibrarySeriesRow } from './library-analysis.models';
import type { HandBrakeQueueFile } from '../utils/user-path.utils';

/* ═══════════════════════════════════════════════════════════════════════════
 * Ultra Library Buddy — shared state model.
 * Single source of truth for: filter state, season-aware status classification,
 * codec composition math, the transcode-savings calculator, KPI view models and
 * Bot-kun action payloads. Consumed by analysis-table, analytics-charts,
 * detail-panel, the Bot-kun assistant and the LibraryIntelStoreService.
 * ═══════════════════════════════════════════════════════════════════════════ */

// ── 1. Filter state & enums ──────────────────────────────────────────────────

/** How selected codec pills must relate to a series' codec composition. */
export type CodecMatchMode =
  | 'any'      // > 0%  — at least one file uses a selected codec (legacy behaviour)
  | 'strict'   // 100%  — every file uses only the selected codec(s)
  | 'mixed'    // <100% — some but not all files use a selected codec (partial transcodes)
  | 'custom';  // custom percentage threshold

export type CodecPercentOp = 'gte' | 'lte';

export interface CodecThreshold {
  mode: CodecMatchMode;
  /** Used when mode === 'custom'. */
  percent: number;
  /** Used when mode === 'custom'. */
  op: CodecPercentOp;
}

/**
 * Season-aware airing/completion status.
 * - seriesEndedComplete : show finished airing AND 100% of episodes on disk
 * - seasonEndedHiatus   : show continuing/renewed but every aired episode is on disk
 *                         (safe to batch transcode)
 * - airingWeekly        : episodes are actively dropping (Sonarr has a next-airing date)
 * - incompleteOnDisk    : aired episodes are missing (downloaded < 100% of aired)
 */
export type LibraryStatusFilter =
  | 'all'
  | 'seriesEndedComplete'
  | 'seasonEndedHiatus'
  | 'airingWeekly'
  | 'incompleteOnDisk'
  /** Composite: seriesEndedComplete OR seasonEndedHiatus — safe to batch transcode. */
  | 'transcodeReady';

/** Per-series derived status (same vocabulary minus 'all'). */
export type SeriesAirStatus = Exclude<LibraryStatusFilter, 'all'>;

export type DownloadFilter = 'all' | '100' | 'partial';
export type HasFilesFilter = 'all' | 'yes' | 'no';
export type SortDirection = 'asc' | 'desc';

/** Complete filter snapshot — serializable, patchable, and the Bot-kun action target. */
export interface LibraryFilterState {
  title: string;
  videoCodec: string[];
  audioCodec: string[];
  resolution: string[];
  codecThreshold: CodecThreshold;
  /** ⚡ Split Codecs Only — series with more than one distinct video codec. */
  splitCodecsOnly: boolean;
  status: LibraryStatusFilter;
  download: DownloadFilter;
  avgEpSize: string;   // preset key, '' = off (kept compatible with the existing UI)
  gibPerHour: string;  // preset key, '' = off
  bitDepth: string;    // '8' | '10' | '' = off
  hasFiles: HasFilesFilter;
  sortCol: string;
  sortDir: SortDirection;
  page: number;
  pageSize: number;
}

export function defaultFilterState(): LibraryFilterState {
  return {
    title: '',
    videoCodec: [],
    audioCodec: [],
    resolution: [],
    codecThreshold: { mode: 'any', percent: 50, op: 'gte' },
    splitCodecsOnly: false,
    status: 'all',
    download: 'all',
    avgEpSize: '',
    gibPerHour: '',
    bitDepth: '',
    hasFiles: 'all',
    sortCol: 'title',
    sortDir: 'asc',
    page: 1,
    pageSize: 20,
  };
}

// ── 2. Season-aware status classification ────────────────────────────────────

export function deriveSeriesStatus(
  row: Pick<LibrarySeriesRow, 'ended' | 'percentDownloaded' | 'nextAiringDate'>,
): SeriesAirStatus {
  if ((row.percentDownloaded ?? 0) < 100) return 'incompleteOnDisk';
  if (row.ended) return 'seriesEndedComplete';
  if (row.nextAiringDate) return 'airingWeekly';
  return 'seasonEndedHiatus';
}

/** True when every aired episode is on disk (safe for batch transcoding). */
export function isTranscodeReady(row: LibrarySeriesRow): boolean {
  const s = deriveSeriesStatus(row);
  return s === 'seriesEndedComplete' || s === 'seasonEndedHiatus';
}

// ── 3. Codec composition math ────────────────────────────────────────────────

const norm = (c: string): string => (c ?? '').trim().toUpperCase().replace('X264', 'H264');

/** Total number of files in the series' video-codec distribution. */
export function codecFileTotal(row: LibrarySeriesRow): number {
  const dist = row.videoCodecDistribution;
  if (dist) return Object.values(dist).reduce((a, b) => a + b, 0);
  return row.primaryVideoCodec ? (row.fileCount ?? 0) : 0;
}

/** Share of FILES using `codec`, in percent (0–100). */
export function codecFileShare(row: LibrarySeriesRow, codec: string): number {
  const total = codecFileTotal(row);
  if (!total) return 0;
  const dist = row.videoCodecDistribution;
  if (!dist) return norm(row.primaryVideoCodec ?? '') === norm(codec) ? 100 : 0;
  const hits = Object.entries(dist)
    .filter(([k]) => norm(k) === norm(codec))
    .reduce((a, [, v]) => a + v, 0);
  return (hits / total) * 100;
}

/** Bytes occupied by files using `codec` (episode-file level; falls back to proportional share). */
export function codecByteTotal(row: LibrarySeriesRow, codec: string): number {
  const files: LibraryEpisodeFileInfo[] | undefined = row.episodeFiles;
  if (files?.length) {
    return files
      .filter(f => norm(f.videoCodec) === norm(codec))
      .reduce((a, f) => a + (f.sizeBytes ?? 0), 0);
  }
  return (row.totalSizeBytes ?? 0) * (codecFileShare(row, codec) / 100);
}

/** Share of BYTES using `codec`, in percent (0–100). */
export function codecSizeShare(row: LibrarySeriesRow, codec: string): number {
  const total = row.totalSizeBytes ?? 0;
  if (!total) return 0;
  return (codecByteTotal(row, codec) / total) * 100;
}

/** ⚡ Split Codecs: more than one distinct video codec across the series' files. */
export function isSplitCodec(row: LibrarySeriesRow): boolean {
  const dist = row.videoCodecDistribution;
  if (dist) return Object.keys(dist).filter(k => dist[k] > 0).length > 1;
  return false;
}

/** Applies the match-mode/threshold semantics to a series' share (%) of a codec. */
export function matchesCodecThreshold(sharePct: number, threshold: CodecThreshold): boolean {
  switch (threshold.mode) {
    case 'any':    return sharePct > 0;
    case 'strict': return sharePct >= 99.9;                 // float-safe 100%
    case 'mixed':  return sharePct > 0 && sharePct < 99.9;
    case 'custom': return threshold.op === 'gte'
      ? sharePct >= threshold.percent
      : sharePct <= threshold.percent;
  }
}

// ── 4. Transcode savings calculator ──────────────────────────────────────────

/** HEVC target: assume ~55% size reduction from H264 at equal perceived quality. */
export const HEVC_SIZE_FACTOR = 0.45;
/** Files above this GiB/h rate are considered bloat worth transcoding. */
export const BLOAT_GIB_PER_HOUR = 1.5;

export function gibPerHourOf(sizeBytes: number, durationSeconds: number): number {
  if (!durationSeconds) return 0;
  return (sizeBytes / 1_073_741_824) / (durationSeconds / 3600);
}

/**
 * Estimated bytes reclaimable for one series — every transcode target
 * (anything but H265/HEVC; UNKNOWN per the toggle) transcoded to HEVC.
 * No bloat gate: the KPI "reclaimable" and the batch "freed" must agree.
 */
export function estimateSeriesSavings(row: LibrarySeriesRow, includeUnknown = true): number {
  const files = row.episodeFiles;
  if (files?.length) {
    return files
      .filter(f => isTranscodeTarget(f.videoCodec, includeUnknown))
      .reduce((a, f) => a + (f.sizeBytes ?? 0) * (1 - HEVC_SIZE_FACTOR), 0);
  }
  // No per-file data: approximate from the series-level target byte shares.
  const distKeys = Object.keys(row.videoCodecDistribution ?? {});
  let targetBytes = 0;
  if (distKeys.length) {
    for (const k of distKeys) {
      if (isTranscodeTarget(k, includeUnknown)) targetBytes += codecByteTotal(row, k);
    }
  } else {
    targetBytes = codecByteTotal(row, 'H264') + (includeUnknown ? codecByteTotal(row, 'UNKNOWN') : 0);
  }
  return targetBytes * (1 - HEVC_SIZE_FACTOR);
}

export interface TranscodeSavings {
  /** Series with at least one reclaimable H264 file. */
  seriesCount: number;
  /** Files targeted. */
  targetFileCount: number;
  /** Bytes currently occupied by the targeted H264 files. */
  h264Bytes: number;
  /** Bytes saved after transcoding to HEVC (≈55%). */
  reclaimableBytes: number;
}

/** Library-wide savings over the SAME transcode targets the batch export uses. */
export function estimateTranscodeSavings(rows: LibrarySeriesRow[], includeUnknown = true): TranscodeSavings {
  let seriesCount = 0, targetFileCount = 0, h264Bytes = 0, reclaimableBytes = 0;
  for (const row of rows) {
    const save = estimateSeriesSavings(row, includeUnknown);
    if (save <= 0) continue;
    seriesCount++;
    reclaimableBytes += save;
    const files = row.episodeFiles;
    if (files?.length) {
      const targets = files.filter(f => isTranscodeTarget(f.videoCodec, includeUnknown));
      targetFileCount += targets.length;
      h264Bytes += targets.reduce((a, f) => a + (f.sizeBytes ?? 0), 0);
    } else {
      const distKeys = Object.keys(row.videoCodecDistribution ?? {});
      const keys = distKeys.length
        ? distKeys.filter(k => isTranscodeTarget(k, includeUnknown))
        : ['H264', ...(includeUnknown ? ['UNKNOWN'] : [])];
      for (const k of keys) {
        h264Bytes += codecByteTotal(row, k);
        targetFileCount += Math.round(codecFileTotal(row) * (codecFileShare(row, k) / 100));
      }
    }
  }
  return { seriesCount, targetFileCount, h264Bytes, reclaimableBytes };
}

// ── 5. KPI view models ───────────────────────────────────────────────────────

export type DonutMode = 'count' | 'size';

export interface DonutSlice {
  label: string;
  value: number; // files or bytes depending on mode
}

export function buildCodecDonut(rows: LibrarySeriesRow[], mode: DonutMode): DonutSlice[] {
  const agg = new Map<string, number>();
  for (const row of rows) {
    const files = row.episodeFiles;
    if (files?.length) {
      for (const f of files) {
        const key = norm(f.videoCodec) || 'UNKNOWN';
        agg.set(key, (agg.get(key) ?? 0) + (mode === 'size' ? (f.sizeBytes ?? 0) : 1));
      }
      continue;
    }
    const dist = row.videoCodecDistribution;
    if (dist) {
      for (const [k, count] of Object.entries(dist)) {
        const key = norm(k) || 'UNKNOWN';
        const add = mode === 'size'
          ? (row.totalSizeBytes ?? 0) * (count / Math.max(1, codecFileTotal(row)))
          : count;
        agg.set(key, (agg.get(key) ?? 0) + add);
      }
    }
  }
  return [...agg.entries()]
    .map(([label, value]) => ({ label, value }))
    .sort((a, b) => b.value - a.value);
}

export type LeaderboardMode = 'totalGb' | 'bloatGibPerHour' | 'reclaimable';

export interface LeaderboardEntry {
  rank: number;
  sonarrId: number;
  title: string;
  codecBadge: string;   // dominant video codec (or "H264/HEVC" when split)
  gibPerHour: number;
  totalSizeBytes: number;
  reclaimableBytes: number;
}

export function buildLeaderboard(rows: LibrarySeriesRow[], mode: LeaderboardMode, limit = 5, includeUnknown = true): LeaderboardEntry[] {
  const entries = rows.map(r => ({
    sonarrId: r.sonarrId,
    title: r.title,
    codecBadge: isSplitCodec(r)
      ? Object.keys(r.videoCodecDistribution ?? {}).map(norm).join('/')
      : norm(r.primaryVideoCodec ?? '—'),
    gibPerHour: r.gibPerHour ?? 0,
    totalSizeBytes: r.totalSizeBytes ?? 0,
    reclaimableBytes: estimateSeriesSavings(r, includeUnknown),
  }));

  const sorted = [...entries].sort((a, b) => {
    switch (mode) {
      case 'totalGb':        return b.totalSizeBytes - a.totalSizeBytes;
      case 'bloatGibPerHour': return b.gibPerHour - a.gibPerHour;
      case 'reclaimable':    return b.reclaimableBytes - a.reclaimableBytes;
    }
  });
  return sorted.slice(0, limit).map((e, i) => ({ ...e, rank: i + 1 }));
}

/** Library-wide storage rate: total bytes / total hours. */
export function libraryAvgGibPerHour(rows: LibrarySeriesRow[]): number {
  const bytes = rows.reduce((a, r) => a + (r.totalSizeBytes ?? 0), 0);
  const seconds = rows.reduce((a, r) => a + (r.totalDurationSeconds ?? 0), 0);
  return gibPerHourOf(bytes, seconds);
}

// ── 6. Bot-kun action payloads ───────────────────────────────────────────────

export type BotKunActionPayload =
  | { type: 'applyFilter'; label: string; filter: Partial<LibraryFilterState> }
  | { type: 'openSeries'; label: string; sonarrId: number };

export const BOT_KUN_ACTION_TAG = 'act';

/**
 * Parses an assistant-message `[act:{json}]` tag into an action payload.
 * Returns null for malformed or unknown payloads (the tag renders as text).
 */
export function parseBotKunActionTag(tagContent: string): BotKunActionPayload | null {
  try {
    const parsed = JSON.parse(tagContent) as Partial<BotKunActionPayload>;
    if (parsed?.type === 'applyFilter' && parsed.filter && typeof parsed.label === 'string') {
      return { type: 'applyFilter', label: parsed.label, filter: parsed.filter };
    }
    if (parsed?.type === 'openSeries' && typeof parsed.sonarrId === 'number' && typeof parsed.label === 'string') {
      return { type: 'openSeries', label: parsed.label, sonarrId: parsed.sonarrId };
    }
    return null;
  } catch {
    return null;
  }
}

/** Compact library summary line injected into Bot-kun's context. */
export function buildBotKunLibraryContext(
  rows: LibrarySeriesRow[],
  savings: TranscodeSavings,
  activeFilterSummary: string,
): string {
  const totalBytes = rows.reduce((a, r) => a + (r.totalSizeBytes ?? 0), 0);
  const gib = (totalBytes / 1_073_741_824).toFixed(1);
  const top = buildLeaderboard(rows, 'bloatGibPerHour', 3)
    .map(e => `${e.title} (${e.gibPerHour.toFixed(2)} GiB/h)`)
    .join('; ');
  return [
    `Library: ${rows.length} series, ${gib} GiB total, avg ${libraryAvgGibPerHour(rows).toFixed(2)} GiB/h.`,
    `Transcode opportunity: ~${(savings.reclaimableBytes / 1_073_741_824).toFixed(1)} GiB reclaimable across ${savings.seriesCount} series.`,
    top ? `Top bloat: ${top}.` : '',
    activeFilterSummary ? `Active table filters: ${activeFilterSummary}.` : '',
  ].filter(Boolean).join(' ');
}

// ── Global transcode batch ─────────────────────────────────────────────────

export type BatchOrder = 'biggest' | 'series';

export interface BatchFile extends HandBrakeQueueFile {
  sonarrId: number;
  gibPerHour: number;
  sizeBytes: number;
}

/**
 * A file is a transcode target when its codec is anything but H265/HEVC —
 * H264, AV1, MPEG2… and optionally UNKNOWN (its real codec is unparsed, so
 * re-encoding is a guess the user can opt into).
 */
export function isTranscodeTarget(codec: string | undefined, includeUnknown: boolean): boolean {
  const c = norm(codec ?? '').replace('X265', 'H265');
  if (c === 'H265' || c === 'HEVC') return false;
  if (!c || c === 'UNKNOWN') return includeUnknown;
  return true;
}

/**
 * Collects every NOT-transcoded episode file across the WHOLE library —
 * deliberately ignoring series/season state so regular batching works for
 * currently-airing shows too. `toHostPath` maps container paths to host paths.
 */
export function collectUntranscodedFiles(
  rows: LibrarySeriesRow[],
  toHostPath: (containerPath: string) => string,
  includeUnknown = true,
): BatchFile[] {
  const out: BatchFile[] = [];
  for (const row of rows) {
    for (const f of row.episodeFiles ?? []) {
      if (!isTranscodeTarget(f.videoCodec, includeUnknown)) continue;
      const size = f.sizeBytes ?? 0;
      out.push({
        sonarrId: row.sonarrId,
        gibPerHour: gibPerHourOf(size, f.durationSeconds ?? 0),
        sizeBytes: size,
        hostSourcePath: toHostPath(`${row.path}/${f.relativePath}`),
        sourceFileName: f.relativePath.split(/[/\\]/).pop() ?? f.relativePath,
        durationSeconds: f.durationSeconds ?? 0,
        resolution: f.resolution ?? '',
        videoCodec: f.videoCodec,
        bitDepth: f.videoBitDepth,
      });
    }
  }
  return out;
}

/** Sorts the batch: biggest rate first, or natural series order (title → path). */
export function sortBatchFiles(files: BatchFile[], order: BatchOrder): BatchFile[] {
  const sorted = [...files];
  if (order === 'biggest') {
    sorted.sort((a, b) => b.gibPerHour - a.gibPerHour || b.sizeBytes - a.sizeBytes);
  } else {
    sorted.sort((a, b) =>
      a.hostSourcePath.localeCompare(b.hostSourcePath, undefined, { numeric: true }));
  }
  return sorted;
}

/** Estimated bytes freed by transcoding the selection to H265 (HEVC_SIZE_FACTOR). */
export function estimateBatchFreedGb(files: { sizeBytes: number }[]): number {
  const bytes = files.reduce((a, f) => a + f.sizeBytes, 0);
  return (bytes * (1 - HEVC_SIZE_FACTOR)) / 1_073_741_824;
}

/** `meta-anime-handbrake-queue-2026-10-29.json` */
export function batchQueueFileName(): string {
  return `meta-anime-handbrake-queue-${new Date().toISOString().slice(0, 10)}.json`;
}

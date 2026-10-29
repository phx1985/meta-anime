/* ═══════════════════════════════════════════════════════════════════════════
 * User path mapping — container paths → local host paths.
 * Sonarr/backend stores container paths like /downloads_root/Vidéos/Animes/…;
 * the user's machine mounts that storage elsewhere (e.g. /mnt/media or Z:\).
 * Pure helpers; the settings-backed wrapper lives in UserPathService.
 * Also hosts the HandBrake queue.json generator (exact import schema).
 * ═══════════════════════════════════════════════════════════════════════════ */

import { HANDBRAKE_JOB_TEMPLATE, HandBrakeQueueEntry } from './handbrake-template.constant';

export type PathSeparatorStyle = 'posix' | 'windows';

/** "dir" = write into DestDir; "transcoded" = _transcoded folder next to each source. */
export type HandBrakeDestMode = 'dir' | 'transcoded';

export interface UserPathMapping {
  /** Container-side media root as stored by Sonarr (e.g. '/downloads_root'). */
  containerRoot: string;
  /** Local host media root (e.g. '/mnt/media', '/run/user/1000/gvfs/…', 'Z:\'). */
  hostRoot: string;
  separator: PathSeparatorStyle;
}

export const DEFAULT_PATH_MAPPING: UserPathMapping = {
  containerRoot: '/downloads_root',
  hostRoot: '',
  separator: 'posix',
};

export const DEFAULT_HANDBRAKE_PRESET_NAME = 'Anime 1080p NVENC VOSTFR (<500MB)';

const sepOf = (style: PathSeparatorStyle): string => (style === 'windows' ? '\\' : '/');

/** True when the container root prefix matches and a host root is configured. */
export function isMappedPath(containerPath: string, mapping: UserPathMapping): boolean {
  const root = mapping.containerRoot.trim().replace(/[\\/]+$/, '');
  return !!root && !!mapping.hostRoot.trim() && !!containerPath && containerPath.startsWith(root);
}

/**
 * Rewrites a container path to the local host path by swapping the root prefix.
 * Returns the input unchanged when the mapping is incomplete or the prefix
 * does not match. Separators inside the remainder follow the target style.
 */
export function resolveUserPath(containerPath: string, mapping: UserPathMapping): string {
  if (!containerPath) return containerPath;
  const root = mapping.containerRoot.trim().replace(/[\\/]+$/, '');
  const host = mapping.hostRoot.trim();
  if (!root || !host || !containerPath.startsWith(root)) return containerPath;

  const remainder = containerPath.slice(root.length).replace(/^[\\/]+/, '');
  const sep = sepOf(mapping.separator);
  const normalizedRemainder = remainder.split(/[\\/]+/).join(sep);
  const hostTrimmed = host.replace(/[\\/]+$/, '');
  return normalizedRemainder ? `${hostTrimmed}${sep}${normalizedRemainder}` : hostTrimmed;
}

/** Resolves every path; convenience for lists. */
export function resolveUserPaths(containerPaths: string[], mapping: UserPathMapping): string[] {
  return containerPaths.map(p => resolveUserPath(p, mapping));
}

/** `"path one.mkv" "path two.mkv"` — one per line for easy terminal use. */
export function quoteCliArgs(hostPaths: string[]): string {
  return hostPaths.map(p => `"${p.replace(/"/g, '\\"')}"`).join('\n');
}

/** `ascendance-of-a-bookworm` from a series title. */
export function seriesSlug(title: string): string {
  return (
    title
      .toLowerCase()
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/(^-|-$)/g, '') || 'series'
  );
}

/** `<series-slug>-handbrake-queue.json` */
export function handBrakeQueueFileName(title: string): string {
  return `${seriesSlug(title)}-handbrake-queue.json`;
}

/** Season folder name ("Season 2") derived from a relative path, else null. */
export function seasonOf(relativePath: string): string | null {
  const m = relativePath.match(/season[ ._-]*(\d+)/i);
  return m ? `Season ${Number(m[1])}` : null;
}

// ── HandBrake queue.json generator ──────────────────────────────────────────

export interface HandBrakeQueueFile {
  /** Resolved HOST source path of the episode file. */
  hostSourcePath: string;
  /** File name with extension (e.g. 'Show - S01E001.WEBDL-1080p.h264.mkv'). */
  sourceFileName: string;
  durationSeconds: number;
  /** Raw resolution label ('1920x1080', '1080p', …). */
  resolution: string;
  /** Display codec from the row data ('H264', 'HEVC', …) → Title.VideoCodec. */
  videoCodec?: string;
  /** Bit depth from the row data → Title.Color.BitDepth. */
  bitDepth?: number;
}

/** '.h264.mkv' or '.mkv' → '.h265.mkv'; other extensions fall back to a swap. */
/** Episode name cleaned of container extension and codec markers ('Bleach S01E01'). */
export function cleanEpisodeNameOf(sourceFileName: string): string {
  const codecToken =
    /[\s._-]*[([]?(?:(?:x264|h264|x265|h265|hevc|avc|av1|mpeg2|mpeg4|vp9|nvenc)(?:[\s._-]?(?:10|8)[\s._-]?bit)?|(?:10|8)[\s._-]?bit)[)\]]?\s*$/i;
  let clean = sourceFileName.replace(/\.(mkv|mp4|avi|m4v|mov|ts)$/i, '');
  let prev: string;
  do {
    prev = clean;
    clean = clean.replace(codecToken, '');
  } while (clean !== prev);
  return clean;
}

export function destFileNameOf(sourceFileName: string): string {
  // Canonical output: '<ep-name>.nvenc.h265.mkv'. Idempotent: re-exporting an
  // output yields the same name.
  return `${cleanEpisodeNameOf(sourceFileName)}.nvenc.h265.mkv`;
}

/** Destination directory per the configured mode. */
export function handBrakeDestDirOf(
  sourceHostPath: string,
  destDir: string,
  mode: HandBrakeDestMode,
): string {
  if (mode === 'transcoded') {
    const slashy = sourceHostPath.replace(/\\/g, '/');
    const idx = slashy.lastIndexOf('/');
    const dir = idx > 0 ? slashy.slice(0, idx) : '.';
    return `${dir}/_transcoded`;
  }
  // Strip trailing separators — replacing them with '.' produced '_transcoded./'.
  return destDir.trim().replace(/[\\/]+$/, '') || '.';
}

/** {Hours, Minutes, Seconds, Ticks} — Ticks = totalSeconds * 90000 (HandBrake). */
export function durationPartsOf(totalSeconds: number): {
  Hours: number;
  Minutes: number;
  Seconds: number;
  Ticks: number;
} {
  const s = Math.max(0, totalSeconds);
  return {
    Hours: Math.floor(s / 3600),
    Minutes: Math.floor((s % 3600) / 60),
    Seconds: Math.floor(s % 60),
    Ticks: s * 90000,
  };
}

/** Width/Height from a resolution label ('1920x1080' | '1080p' | …). */
export function geometryOf(resolution: string): { Width: number; Height: number } {
  const raw = (resolution ?? '').trim();
  const exact = raw.match(/^(\d{3,4})\s*[x×]\s*(\d{3,4})$/i);
  if (exact) return { Width: +exact[1], Height: +exact[2] };
  const p = raw.match(/^(\d{3,4})\s*p$/i);
  if (p) {
    const h = +p[1];
    const w = h >= 2000 ? 3840 : h >= 1000 ? 1920 : h >= 700 ? 1280 : h >= 540 ? 960 : 854;
    return { Width: w, Height: h };
  }
  return { Width: 0, Height: 0 };
}

/** Fixed video parameters (per the HandBrake export specification). */
export const HANDBRAKE_VIDEO_OPTIONS =
  'multipass=fullres:rc-lookahead=32:bf=4:b_ref_mode=middle:spatial-aq=1:temporal-aq=1:aq-strength=8:maxrate=2500k:bufsize=5000k';

/**
 * Builds a HandBrake-queue-importable array using the TEMPLATE CLONE pattern:
 * every entry is structuredClone(HANDBRAKE_JOB_TEMPLATE) (a verified full-state
 * snapshot — HandBrake GTK segfaults on missing uiSettings/Title keys) with ONLY
 * the dynamic paths mutated:
 *   Job.SequenceID (0-based), Job.Source.{Path, Title: always 1}, Job.Destination.File,
 *   Job.Metadata.{Name,CreationTime}, Job.Video.{Options,Quality}
 *   Title.{Path,Name,Index,Metadata,Duration,Geometry}
 *   uiSettings.{source,default_source,destination,dest_dir,dest_file,
 *               source_label,volume,title,PresetName,PresetFullName}
 */
export function buildHandBrakeQueue(
  files: HandBrakeQueueFile[],
  destDir: string,
  destMode: HandBrakeDestMode,
  presetName: string,
  options?: { videoOptions?: string; videoQuality?: number },
): HandBrakeQueueEntry[] {
  const name = presetName.trim() || DEFAULT_HANDBRAKE_PRESET_NAME;

  return files.map((file, index) => {
    const entry: HandBrakeQueueEntry = structuredClone(HANDBRAKE_JOB_TEMPLATE);
    // The template is a deep dynamic snapshot — mutate the known dynamic paths
    // via loose handles (the key structure itself is frozen in the constant).
    /* eslint-disable @typescript-eslint/no-explicit-any */
    const job = entry.Job as any;
    const title = entry.Title as any;
    const ui = entry.uiSettings as any;
    /* eslint-enable @typescript-eslint/no-explicit-any */

    const sourcePath = file.hostSourcePath;
    const destFile = destFileNameOf(file.sourceFileName);
    const outDir = handBrakeDestDirOf(sourcePath, destDir, destMode);
    const destFull = `${outDir}/${destFile}`;
    const baseName = cleanEpisodeNameOf(file.sourceFileName);
    const now = new Date().toISOString();
    const geo = geometryOf(file.resolution);
    const durationObj = durationPartsOf(file.durationSeconds);
    // Time range over the whole episode in 90 kHz ticks, +5s safety buffer so
    // millisecond rounding never clips the last frame. (A chapter range stops
    // at Chapter 1 — the ~45s cold open — on any MKV that has chapters.)
    const endTicks = Math.ceil((file.durationSeconds + 5) * 90000);

    // ── Job ──────────────────────────────────────────────────────────────
    job.SequenceID = index;
    job.Source.Path = sourcePath;
    // Standalone video files (MKV/MP4) ALWAYS use Title 1 — never the index.
    job.Source.Title = 1;
    job.Source.Range = { Type: 'time', Start: 0, End: endTicks };
    job.Destination.File = destFull;
    // Keep the MKV's own chapters: never fake a chapter list here.
    job.Destination.ChapterMarkers = false;
    job.Destination.ChapterList = [];
    job.Metadata.Name = baseName;
    job.Metadata.CreationTime = now;
    job.Video.Options = options?.videoOptions ?? HANDBRAKE_VIDEO_OPTIONS;
    job.Video.Quality = options?.videoQuality ?? 26.0;

    // ── Title ────────────────────────────────────────────────────────────
    title.Path = sourcePath;
    title.ChapterList = [];
    title.Name = baseName;
    title.Index = 1;
    title.Metadata.Name = baseName;
    // Codec + bit depth from the file's row data when known (template fallback).
    title.VideoCodec = codecSlugOf(file.videoCodec) ?? title.VideoCodec;
    if (file.bitDepth) title.Color.BitDepth = file.bitDepth;
    title.Metadata.CreationTime = now;
    title.Duration = durationObj;
    title.Geometry = {
      Height: geo.Height || 1080,
      PAR: { Den: 1, Num: 1 },
      Width: geo.Width || 1920,
    };

    // ── uiSettings ───────────────────────────────────────────────────────
    ui.source = sourcePath;
    ui.default_source = sourcePath;          // legacy alias (kept in Preferences too)
    ui.Preferences.default_source = sourcePath;
    ui.destination = destFull;
    ui.dest_dir = outDir;
    ui.dest_file = destFile;
    ui.source_label = baseName;
    ui.volume = baseName;
    ui.title = 1;
    // Mirror the Job's time range in the UI state.
    ui.PtoPType = 'time';
    ui.start_point = 0;
    ui.end_point = Math.ceil(file.durationSeconds + 5);
    if ('ChapterMarkers' in ui) ui.ChapterMarkers = false;
    if ('MetaName' in ui) ui.MetaName = baseName;
    else if ('MetaName' in job.Metadata) job.Metadata.MetaName = baseName;
    ui.PresetName = name;
    ui.PresetFullName = `/My Presets/${name}`;

    return entry;
  });
}

/** Display codec ('H264', 'HEVC', …) → HandBrake title slug ('h264', 'hevc', …). */
function codecSlugOf(codec?: string): string | undefined {
  if (!codec) return undefined;
  const slug: Record<string, string> = {
    H264: 'h264', X264: 'h264', AVC: 'h264',
    HEVC: 'hevc', H265: 'hevc', X265: 'hevc',
    AV1: 'av1', MPEG2: 'mpeg2', MPEG4: 'mpeg4', VP9: 'vp9',
  };
  return slug[codec.trim().toUpperCase()];
}

/** Serializes the queue exactly as HandBrake expects to import it. */
export function serializeHandBrakeQueue(queue: unknown[]): string {
  return JSON.stringify(queue, null, 2);
}

/** Format elapsed milliseconds as a human-readable string: "123 ms", "4.5 s", "1m 23s" */
export function fmtElapsed(ms: number): string {
  if (ms < 1000) return `${ms} ms`;
  const s = ms / 1000;
  if (s < 60) return `${s.toFixed(1)} s`;
  const m = Math.floor(s / 60);
  return `${m}m ${Math.floor(s % 60)}s`;
}

/**
 * Format raw bytes to a human-readable size string using Base-2 (1024) math.
 * @param bytes - size in bytes
 * @param translateFn - function to translate unit keys ('common.units.gb', 'common.units.mb', 'common.units.kb')
 */
export function formatTableSize(bytes: number | undefined, translateFn: (key: string) => string): string {
  if (!bytes || bytes <= 0) return '—';
  const GiB = 1024 ** 3;
  const MiB = 1024 ** 2;
  const KiB = 1024;
  if (bytes >= GiB) return (bytes / GiB).toFixed(1) + ' ' + translateFn('common.units.gb');
  if (bytes >= MiB) return (bytes / MiB).toFixed(1) + ' ' + translateFn('common.units.mb');
  return (bytes / KiB).toFixed(0) + ' ' + translateFn('common.units.kb');
}

/**
 * Format raw bytes with BINARY units and localized labels — "1.42 GiB" in English,
 * "1.42 Gio" in French (KiB/MiB/GiB/TiB vs Kio/Mio/Gio/Tio).
 * @param bytes - size in bytes
 * @param translateFn - function to translate unit keys ('common.units.gib', …)
 */
export function formatBinarySize(bytes: number | undefined, translateFn: (key: string) => string): string {
  if (!bytes || bytes <= 0) return '—';
  const TiB = 1024 ** 4;
  const GiB = 1024 ** 3;
  const MiB = 1024 ** 2;
  const KiB = 1024;
  if (bytes >= TiB) return (bytes / TiB).toFixed(2) + ' ' + translateFn('common.units.tib');
  if (bytes >= GiB) return (bytes / GiB).toFixed(2) + ' ' + translateFn('common.units.gib');
  if (bytes >= MiB) return (bytes / MiB).toFixed(0) + ' ' + translateFn('common.units.mib');
  return (bytes / KiB).toFixed(0) + ' ' + translateFn('common.units.kib');
}

/**
 * Convert a codec distribution map into a sorted array of { label, pct } pills.
 * Filters out empty/null keys and entries with 0 count.
 */
export function getCodecPills(dist?: { [key: string]: number }): { label: string; pct: number }[] {
  if (!dist) return [];
  const entries = Object.entries(dist).filter(([key, count]) => key?.trim() && count > 0);
  const total = entries.reduce((s, [, v]) => s + v, 0);
  if (total === 0) return [];
  return entries
    .map(([label, count]) => ({
      label: label.toUpperCase(),
      pct: Math.round((count / total) * 100),
    }))
    .sort((a, b) => b.pct - a.pct);
}

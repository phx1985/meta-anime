/** Format elapsed milliseconds as a human-readable string: "123 ms", "4.5 s", "1m 23s" */
export function fmtElapsed(ms: number): string {
  if (ms < 1000) return `${ms} ms`;
  const s = ms / 1000;
  if (s < 60) return `${s.toFixed(1)} s`;
  const m = Math.floor(s / 60);
  return `${m}m ${Math.floor(s % 60)}s`;
}

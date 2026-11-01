/**
 * Chart theming helpers.
 *
 * Chart.js cannot consume CSS custom properties directly, so semantic token
 * values are resolved from :root via getComputedStyle at chart build time.
 * Charts rebuild when the theme changes (see the chart components), which is
 * what keeps them theme-aware — no literal colors live in chart configuration.
 */

/** Resolve a semantic CSS custom property from the document root. */
export function readToken(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

const PALETTE_TOKENS = [
  '--color-brand',
  '--accent-primary',
  '--color-success-deep',
  '--color-warning-deep',
  '--color-danger-deep',
  '--color-success',
  '--color-warning',
  '--color-danger',
];

/**
 * Resolve `count` distinct series colors from semantic tokens. Entries beyond
 * the token set are mixed toward the elevated surface so slices stay distinct
 * without inventing raw colors. The computed style is resolved once per call
 * instead of once per token read.
 */
export function chartPalette(count: number): string[] {
  const style = getComputedStyle(document.documentElement);
  const read = (name: string): string => style.getPropertyValue(name).trim();
  const surface = read('--color-surface-elevated');
  return Array.from({ length: count }, (_, i) => {
    const base = read(PALETTE_TOKENS[i % PALETTE_TOKENS.length]);
    return i < PALETTE_TOKENS.length ? base : mixColors(base, surface, 0.35);
  });
}

/** Mix two hex token values (t = weight of `a`); falls back to `a` on parse failure. */
export function mixColors(a: string, b: string, t: number): string {
  const ca = parseHex(a);
  const cb = parseHex(b);
  if (!ca || !cb) return a;
  const m = (x: number, y: number): number => Math.round(x * t + y * (1 - t));
  return `#${[m(ca[0], cb[0]), m(ca[1], cb[1]), m(ca[2], cb[2])]
    .map((v) => v.toString(16).padStart(2, '0'))
    .join('')}`;
}

/** Render a hex token value as an rgba() string with the given alpha. */
export function withAlpha(hex: string, alpha: number): string {
  const c = parseHex(hex);
  if (!c) return hex;
  return `rgba(${c[0]}, ${c[1]}, ${c[2]}, ${alpha})`;
}

function parseHex(value: string): [number, number, number] | null {
  const v = value.replace('#', '');
  if (/^[0-9a-fA-F]{3}$/.test(v)) {
    return [parseInt(v[0] + v[0], 16), parseInt(v[1] + v[1], 16), parseInt(v[2] + v[2], 16)];
  }
  if (/^[0-9a-fA-F]{6}$/.test(v)) {
    return [parseInt(v.slice(0, 2), 16), parseInt(v.slice(2, 4), 16), parseInt(v.slice(4, 6), 16)];
  }
  return null;
}

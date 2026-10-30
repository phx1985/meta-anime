/**
 * Decides whether an HttpClient request targets our own backend and may therefore
 * receive credentials (JWT Bearer / X-Api-Key).
 *
 * The app's API base is same-origin (services use relative '/api/v1/…' paths; there is
 * no cross-origin API base in the environment files), so a request qualifies when its
 * URL is relative, or absolute on the current page origin. Anything cross-origin must
 * never receive credentials.
 */
export function targetsOwnApi(requestUrl: string): boolean {
  // Relative URLs ('/api/v1/…', 'assets/config.json') always hit our own origin.
  if (!/^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(requestUrl) && !requestUrl.startsWith('//')) {
    return true;
  }
  // Protocol-relative ('//host/…') and absolute URLs qualify only on the page origin.
  if (typeof location === 'undefined') {
    return false;
  }
  try {
    return new URL(requestUrl, location.href).origin === location.origin;
  } catch {
    return false;
  }
}

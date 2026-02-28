import { Injectable, inject, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser, DOCUMENT } from '@angular/common';
import { MobileDetectionService } from './mobile-detection.service';

/**
 * Handles opening Plex links with native-app deep linking on mobile devices.
 *
 * - Desktop: opens the standard Plex web URL in a new tab.
 * - Mobile: attempts to launch the native Plex app via the `plex://` URI scheme.
 *   If the app is not installed (or the deep link does not execute within 500 ms),
 *   it gracefully falls back to opening the web URL in the browser — the click
 *   is never a "dead end".
 */
@Injectable({ providedIn: 'root' })
export class PlexDeepLinkService {
  private platformId = inject(PLATFORM_ID);
  private document = inject(DOCUMENT);
  private mobileDetection = inject(MobileDetectionService);

  /**
   * Opens `webUrl` using the appropriate strategy:
   * - Desktop → `window.open(webUrl, '_blank', 'noopener,noreferrer')`
   * - Mobile  → plex:// deep link, with a 500 ms fallback to the web URL.
   */
  openLink(webUrl: string | null | undefined): void {
    if (!webUrl || !isPlatformBrowser(this.platformId)) return;

    if (!this.mobileDetection.isMobile()) {
      this.document.defaultView?.open(webUrl, '_blank', 'noopener,noreferrer');
      return;
    }

    const deepLinkUri = this.buildPlexUri(webUrl);

    // Listen for the page becoming hidden — that signals the native app took over.
    let fallbackTimer: ReturnType<typeof setTimeout>;

    const onVisibilityChange = (): void => {
      if (this.document.hidden) {
        clearTimeout(fallbackTimer);
      }
    };

    this.document.addEventListener('visibilitychange', onVisibilityChange, { once: true });

    // Attempt to open the native Plex app.
    this.document.location.href = deepLinkUri;

    // If the page is still visible after 500 ms, the app was not installed — open web URL.
    fallbackTimer = setTimeout(() => {
      this.document.removeEventListener('visibilitychange', onVisibilityChange);
      if (!this.document.hidden) {
        this.document.defaultView?.open(webUrl, '_blank', 'noopener,noreferrer');
      }
    }, 500);
  }

  /**
   * Converts a Plex web URL to a native `plex://` URI scheme URL.
   *
   * Example conversion:
   * `https://app.plex.tv/desktop/#!/server/{machineId}/details?key=/library/metadata/123`
   * → `plex://preplay?key=%2Flibrary%2Fmetadata%2F123&server={machineId}`
   *
   * Falls back to `plex://` (opens the Plex home screen) when the URL does not
   * match the expected deep-link pattern.
   */
  buildPlexUri(webUrl: string): string {
    try {
      const url = new URL(webUrl);
      // Hash is like #!/server/{machineId}/details?key=/library/metadata/123
      const hash = url.hash.replace(/^#!?/, '');
      const match = hash.match(/^\/server\/([^/?#]+)\/details(.*)/);
      if (match) {
        const serverId = match[1];
        const query = match[2] ?? '';
        const params = new URLSearchParams(query.replace(/^\?/, ''));
        const key = params.get('key');
        if (key) {
          return `plex://preplay?key=${encodeURIComponent(key)}&server=${encodeURIComponent(serverId)}`;
        }
      }
    } catch {
      // Malformed URL — fall through to default.
    }
    // Generic Plex deep link: opens the Plex home screen in the native app.
    return 'plex://';
  }
}

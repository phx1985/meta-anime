import { Injectable, inject, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser, DOCUMENT } from '@angular/common';
import { MobileDetectionService } from './mobile-detection.service';
import { ProviderStateService } from './provider-state.service';

/**
 * Handles opening Plex links with native-app deep linking on mobile devices.
 *
 * - Desktop: opens the standard Plex Universal Link in a new tab.
 * - Mobile: attempts to launch the native Plex app via the `plex://` URI scheme.
 *   The window `blur` event is used to detect when the native app has taken
 *   focus (more reliable than `visibilitychange` on iOS/Android), preventing the
 *   "double-open" bug where both the native app AND a new browser tab open
 *   simultaneously.  If the app is not installed (page stays focused after
 *   1 000 ms), the service gracefully falls back to opening the web URL in the
 *   browser — the click is never a "dead end".
 */
@Injectable({ providedIn: 'root' })
export class PlexDeepLinkService {
  private platformId = inject(PLATFORM_ID);
  private document = inject(DOCUMENT);
  private mobileDetection = inject(MobileDetectionService);
  private providerState = inject(ProviderStateService);

  /**
   * Builds the canonical Plex Universal Link for a specific show.
   *
   * Format: `https://app.plex.tv/desktop/#!/server/{machineId}/details?key=%2Flibrary%2Fmetadata%2F{ratingKey}`
   *
   * iOS and Android intercept `https://app.plex.tv` Universal Links and open
   * them directly in the native Plex app when it is installed.
   */
  buildUniversalLink(machineId: string, ratingKey: string): string {
    return `https://app.plex.tv/desktop/#!/server/${encodeURIComponent(machineId)}/details?key=%2Flibrary%2Fmetadata%2F${ratingKey}`;
  }

  /**
   * Opens `webUrl` using the appropriate strategy:
   * - Desktop → `window.open(correctedUrl, '_blank', 'noopener,noreferrer')`
   * - Mobile  → plex:// deep link, with a 1 000 ms fallback to the web URL.
   *
   * The URL is always validated and corrected to the Universal Link format
   * before use, using the machine identifier stored in settings.
   */
  openLink(webUrl: string | null | undefined, title?: string): void {
    if (!webUrl || !isPlatformBrowser(this.platformId)) return;

    const storedMachineId = this.providerState.plexMachineId();
    const correctedUrl = storedMachineId
      ? this.ensureUniversalLink(webUrl, storedMachineId)
      : webUrl;

    if (!this.mobileDetection.isMobile()) {
      this.document.defaultView?.open(correctedUrl, '_blank', 'noopener,noreferrer');
      return;
    }

    const deepLinkUri = this.buildPlexUri(correctedUrl);
    const win = this.document.defaultView;
    let fallbackTimer: ReturnType<typeof setTimeout>;

    // `blur` fires when the browser window loses focus — i.e. the native Plex
    // app came to the foreground.  This is more reliable than `visibilitychange`
    // on mobile Safari/Chrome which may not fire immediately.
    const onBlur = (): void => {
      clearTimeout(fallbackTimer);
    };

    win?.addEventListener('blur', onBlur, { once: true });

    // Attempt to open the native Plex app without navigating the current page.
    this.document.location.href = deepLinkUri;

    // If the window is still focused after 1 000 ms the app was not installed —
    // open the web URL as fallback.
    fallbackTimer = setTimeout(() => {
      win?.removeEventListener('blur', onBlur);
      if (!this.document.hidden) {
        win?.open(correctedUrl, '_blank', 'noopener,noreferrer');
      }
    }, 1000);
  }

  /**
   * Converts a Plex web URL to the native `plex://preplay/` URI scheme for
   * opening a specific library item's detail page directly in the Plex app.
   *
   * Format:
   * `plex://preplay/?metadataKey=%2Flibrary%2Fmetadata%2F{ratingKey}&server={machineId}`
   *
   * The `metadataKey` is the full `/library/metadata/{id}` path, URL-encoded.
   * The `server` is the 40-character hex machine identifier.
   *
   * Falls back to `plex://` (Plex home screen) when the key or server ID
   * cannot be resolved.
   */
  buildPlexUri(webUrl: string): string {
    const storedMachineId = this.providerState.plexMachineId();
    try {
      const url = new URL(webUrl);
      // Hash format: #!/server/{machineId}/details?key=/library/metadata/123
      const hash = url.hash.replace(/^#!?/, '');
      const match = hash.match(/^\/server\/([^/?#]*?)\/details(.*)/);
      if (match) {
        const serverId = match[1] || storedMachineId || '';
        const query = match[2] ?? '';
        // URLSearchParams automatically decodes percent-encoded values (e.g. %2F → /)
        const key = new URLSearchParams(query.replace(/^\?/, '')).get('key');
        if (key && serverId) {
          // Re-encode the full path so slashes become %2F in the output
          return `plex://preplay/?metadataKey=${encodeURIComponent(key)}&server=${serverId}`;
        }
      }
    } catch {
      // Malformed URL — fall through to generic fallback.
    }
    return 'plex://';
  }

  /**
   * Ensures `webUrl` is in the correct `https://app.plex.tv/desktop/#!/server/...`
   * Universal Link format. If the URL has a missing or empty machine identifier,
   * it is rebuilt using the stored `machineId`.
   */
  private ensureUniversalLink(webUrl: string, machineId: string): string {
    try {
      const url = new URL(webUrl);
      const hash = url.hash.replace(/^#!?/, '');
      const match = hash.match(/^\/server\/([^/?#]+)\/details(.*)/);
      if (match && match[1]) {
        // Already a valid Universal Link with a non-empty machine ID.
        return webUrl;
      }
      // Extract the ratingKey from the URL and rebuild with the correct machine ID.
      const keyStr = hash.split('?')[1] ?? url.search.replace(/^\?/, '');
      const key = new URLSearchParams(keyStr).get('key') ?? '';
      const ratingKey =
        key.match(/\/metadata\/([^/?]+)/)?.[1] ??
        decodeURIComponent(webUrl).match(/\/metadata\/(\d+)/)?.[1] ??
        '';
      if (ratingKey) {
        return this.buildUniversalLink(machineId, ratingKey);
      }
    } catch {
      /* malformed URL — return original */
    }
    return webUrl;
  }
}

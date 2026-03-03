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
   *
   * @param title  Optional anime title used as a search fallback when the
   *               ratingKey cannot be extracted from `webUrl`.
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

    const deepLinkUri = this.buildPlexUri(correctedUrl, title);
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
   * Converts a Plex web URL to a native `plex://` URI scheme URL that opens the
   * **details page** for a specific library item directly inside the Plex app.
   *
   * Example conversion:
   * `https://app.plex.tv/desktop/#!/server/{machineId}/details?key=/library/metadata/123`
   * → `plex://server/{machineId}/details?key=%2Flibrary%2Fmetadata%2F123`
   *
   * Uses the machine identifier stored in settings as a fallback when the URL
   * has a missing or empty server ID (e.g. when machine ID was not yet persisted).
   *
   * Search fallback (when ratingKey cannot be extracted but a title is provided):
   * → `plex://server/all/details?search={encodedTitle}`
   *
   * Falls back to `plex://` (opens the Plex home screen) when neither the URL
   * matches the expected deep-link pattern nor a title is provided.
   */
  buildPlexUri(webUrl: string, title?: string): string {
    const storedMachineId = this.providerState.plexMachineId();
    try {
      const url = new URL(webUrl);
      // Hash is like #!/server/{machineId}/details?key=/library/metadata/123
      const hash = url.hash.replace(/^#!?/, '');
      const match = hash.match(/^\/server\/([^/?#]*?)\/details(.*)/);
      if (match) {
        // Use the stored machine ID as fallback if the URL has an empty one
        const serverId = match[1] || storedMachineId || '';
        const query = match[2] ?? '';
        const params = new URLSearchParams(query.replace(/^\?/, ''));
        const key = params.get('key');
        if (key && serverId) {
          // plex://server/{machineId}/details?key={ratingKey} is the native URI
          // scheme recognised by both the iOS and Android Plex apps to navigate
          // directly to a specific show's details page.
          return `plex://server/${encodeURIComponent(serverId)}/details?key=${encodeURIComponent(key)}`;
        }
      }
    } catch {
      // Malformed URL — fall through to search fallback or default.
    }
    // Search fallback: navigate to the item via title search in the native app.
    if (title) {
      return `plex://server/all/details?search=${encodeURIComponent(title)}`;
    }
    // Generic Plex deep link: opens the Plex home screen in the native app.
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

import { Injectable, inject, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

/**
 * Provides robust mobile-device detection based on the User-Agent string.
 * This complements CSS-only responsive design with behavioral JavaScript checks
 * (e.g. to decide whether to attempt native-app deep links).
 */
@Injectable({ providedIn: 'root' })
export class MobileDetectionService {
  private platformId = inject(PLATFORM_ID);

  /**
   * Returns `true` when the user is on an iOS or Android mobile device.
   * Detection is based on the User-Agent string; it always returns `false`
   * in server-side rendering environments.
   */
  isMobile(): boolean {
    if (!isPlatformBrowser(this.platformId)) return false;
    return /android|iphone|ipad|ipod/i.test(navigator.userAgent);
  }
}

import { Pipe, PipeTransform, inject } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';

@Pipe({ name: 'safe', standalone: true })
export class SafePipe implements PipeTransform {
  private sanitizer = inject(DomSanitizer);

  /**
   * Trusts a trailer/embed URL only when it is an absolute http(s) URL with a real host
   * (this covers known embed hosts such as youtube-nocookie.com/embed/…). Anything else —
   * relative or protocol-relative URLs, javascript:, data:, malformed strings — returns
   * `null` so the resource-URL binding renders nothing instead of trusting MAL-sourced
   * input blindly. Valid URLs are trusted exactly as before.
   */
  transform(url: string | null | undefined): SafeResourceUrl | null {
    if (!url) {
      return null;
    }
    let parsed: URL;
    try {
      parsed = new URL(url);
    } catch {
      return null; // not an absolute URL
    }
    if ((parsed.protocol !== 'https:' && parsed.protocol !== 'http:') || !parsed.hostname) {
      return null;
    }
    return this.sanitizer.bypassSecurityTrustResourceUrl(url);
  }
}

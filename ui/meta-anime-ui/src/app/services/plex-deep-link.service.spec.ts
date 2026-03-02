import { TestBed } from '@angular/core/testing';
import { PLATFORM_ID } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { PlexDeepLinkService } from './plex-deep-link.service';
import { MobileDetectionService } from './mobile-detection.service';

describe('PlexDeepLinkService', () => {
  let service: PlexDeepLinkService;
  let mockDocument: Partial<Document>;
  let openSpy: ReturnType<typeof vi.fn>;
  let mockMobileDetection: { isMobile: ReturnType<typeof vi.fn> };
  let mockWindow: Partial<Window & typeof globalThis>;

  function setup(isMobile: boolean): void {
    openSpy = vi.fn();
    mockWindow = {
      open: openSpy,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    } as unknown as Window & typeof globalThis;
    mockMobileDetection = { isMobile: vi.fn().mockReturnValue(isMobile) };
    mockDocument = {
      defaultView: mockWindow as Window & typeof globalThis,
      location: { href: '' } as Location,
      hidden: false,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        PlexDeepLinkService,
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: DOCUMENT, useValue: mockDocument },
        { provide: MobileDetectionService, useValue: mockMobileDetection },
      ],
    });

    service = TestBed.inject(PlexDeepLinkService);
  }

  afterEach(() => TestBed.resetTestingModule());

  // ── buildPlexUri ─────────────────────────────────────────────────────────

  describe('buildPlexUri', () => {
    beforeEach(() => setup(false));

    it('converts a full Plex desktop URL to a plex://server/ mobile deep link', () => {
      const webUrl =
        'https://app.plex.tv/desktop/#!/server/abc123/details?key=%2Flibrary%2Fmetadata%2F456';
      const result = service.buildPlexUri(webUrl);
      expect(result).toBe(
        'plex://preplay/?metadataKey=%2Flibrary%2Fmetadata%2F456&metadataType=4&server=abc123',
      );
    });

    it('returns plex:// for a plain https://app.plex.tv URL with no hash and no title', () => {
      expect(service.buildPlexUri('https://app.plex.tv')).toBe('plex://');
    });

    it('returns search fallback when no ratingKey but title is provided', () => {
      expect(service.buildPlexUri('https://app.plex.tv', 'Attack on Titan')).toBe(
        'plex://server/all/details?search=Attack%20on%20Titan',
      );
    });

    it('returns plex:// for a malformed URL with no title', () => {
      expect(service.buildPlexUri('not-a-url')).toBe('plex://');
    });

    it('returns search fallback for a malformed URL when title is provided', () => {
      expect(service.buildPlexUri('not-a-url', 'Naruto')).toBe(
        'plex://server/all/details?search=Naruto',
      );
    });

    it('returns plex:// when hash path does not match expected pattern', () => {
      expect(service.buildPlexUri('https://app.plex.tv/#!/other/path')).toBe('plex://');
    });

    it('returns search fallback when hash does not match but title is provided', () => {
      expect(service.buildPlexUri('https://app.plex.tv/#!/other/path', 'One Piece')).toBe(
        'plex://server/all/details?search=One%20Piece',
      );
    });
  });

  // ── openLink (desktop) ───────────────────────────────────────────────────

  describe('openLink on desktop', () => {
    beforeEach(() => setup(false));

    it('opens the web URL in a new tab on desktop', () => {
      service.openLink('https://app.plex.tv/desktop/#!/server/abc/details?key=%2F1');
      expect(openSpy).toHaveBeenCalledWith(
        'https://app.plex.tv/desktop/#!/server/abc/details?key=%2F1',
        '_blank',
        'noopener,noreferrer',
      );
    });

    it('does nothing when url is null', () => {
      service.openLink(null);
      expect(openSpy).not.toHaveBeenCalled();
    });

    it('does nothing when url is empty string', () => {
      service.openLink('');
      expect(openSpy).not.toHaveBeenCalled();
    });
  });

  // ── openLink (mobile) ────────────────────────────────────────────────────

  describe('openLink on mobile', () => {
    beforeEach(() => setup(true));

    it('sets document.location.href to the plex://server/ mobile deep link URI on mobile', () => {
      const webUrl = 'https://app.plex.tv/desktop/#!/server/srv1/details?key=%2Flib%2F1';
      service.openLink(webUrl);
      expect(mockDocument.location!.href).toBe(
        'plex://preplay/?metadataKey=%2Flib%2F1&metadataType=4&server=srv1',
      );
    });

    it('registers a blur listener on the window to cancel the fallback', () => {
      service.openLink('https://app.plex.tv');
      expect(mockWindow.addEventListener).toHaveBeenCalledWith(
        'blur',
        expect.any(Function),
        { once: true },
      );
    });

    it('falls back to the web URL after 1 000 ms if page is still visible', async () => {
      vi.useFakeTimers();
      service.openLink('https://app.plex.tv');
      // Simulate page remaining visible
      vi.advanceTimersByTime(1000);
      expect(openSpy).toHaveBeenCalledWith(
        'https://app.plex.tv',
        '_blank',
        'noopener,noreferrer',
      );
      vi.useRealTimers();
    });

    it('does NOT fall back if the page became hidden (app opened)', async () => {
      vi.useFakeTimers();
      // Make document.hidden return true → app opened
      Object.defineProperty(mockDocument, 'hidden', { value: true, configurable: true });
      service.openLink('https://app.plex.tv');
      vi.advanceTimersByTime(1000);
      expect(openSpy).not.toHaveBeenCalled();
      vi.useRealTimers();
    });
  });
});

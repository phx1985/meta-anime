import { TestBed } from '@angular/core/testing';
import { PLATFORM_ID } from '@angular/core';
import { MobileDetectionService } from './mobile-detection.service';

describe('MobileDetectionService', () => {
  function createService(platformId: string, userAgent: string): MobileDetectionService {
    Object.defineProperty(navigator, 'userAgent', { value: userAgent, configurable: true });
    TestBed.configureTestingModule({
      providers: [
        MobileDetectionService,
        { provide: PLATFORM_ID, useValue: platformId },
      ],
    });
    return TestBed.inject(MobileDetectionService);
  }

  afterEach(() => TestBed.resetTestingModule());

  it('returns false in a server-side rendering context', () => {
    const service = createService('server', 'Mozilla/5.0');
    expect(service.isMobile()).toBe(false);
  });

  it('returns false for a typical desktop Chrome UA', () => {
    const service = createService(
      'browser',
      'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36',
    );
    expect(service.isMobile()).toBe(false);
  });

  it('returns true for an Android UA', () => {
    const service = createService(
      'browser',
      'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 Chrome/124.0.0.0 Mobile Safari/537.36',
    );
    expect(service.isMobile()).toBe(true);
  });

  it('returns true for an iPhone UA', () => {
    const service = createService(
      'browser',
      'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Version/17.0 Mobile/15E148 Safari/604.1',
    );
    expect(service.isMobile()).toBe(true);
  });

  it('returns true for an iPad UA', () => {
    const service = createService(
      'browser',
      'Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Version/17.0 Mobile/15E148 Safari/604.1',
    );
    expect(service.isMobile()).toBe(true);
  });
});

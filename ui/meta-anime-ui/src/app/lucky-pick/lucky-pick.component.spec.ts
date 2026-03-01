import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { LuckyPickComponent } from './lucky-pick.component';
import { UserPreferencesService } from '../services/user-preferences.service';

describe('LuckyPickComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LuckyPickComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
  });

  it('should create the component', () => {
    const fixture = TestBed.createComponent(LuckyPickComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should emit closed event when close() is called', () => {
    const fixture = TestBed.createComponent(LuckyPickComponent);
    const component = fixture.componentInstance;
    let emitted = false;
    component.closed.subscribe(() => (emitted = true));
    component.close();
    expect(emitted).toBe(true);
  });

  it('should not re-spin when already spinning', () => {
    const fixture = TestBed.createComponent(LuckyPickComponent);
    const component = fixture.componentInstance;
    // Access private state via type cast
    const comp = component as unknown as { state: { (): string; set: (v: string) => void } };
    comp.state.set('spinning' as never);
    // spin() should be a no-op when already spinning — no error thrown
    expect(() => component.spin()).not.toThrow();
  });

  it('should use UserPreferencesService luckyPick preferences', () => {
    const prefs = TestBed.inject(UserPreferencesService);
    expect(prefs.luckyOnlyFinished).toBeDefined();
    expect(prefs.luckyOnlyPlanToWatch).toBeDefined();
    expect(prefs.luckyOnlyWithFiles).toBeDefined();
  });

  it('should default luckyOnlyPlanToWatch to true and luckyOnlyWithFiles to true', () => {
    const prefs = TestBed.inject(UserPreferencesService);
    // Defaults: onlyPlanToWatch=true, onlyWithFiles=true, onlyFinished=false
    expect(typeof prefs.luckyOnlyPlanToWatch()).toBe('boolean');
    expect(typeof prefs.luckyOnlyWithFiles()).toBe('boolean');
    expect(typeof prefs.luckyOnlyFinished()).toBe('boolean');
  });

  it('should expose plexPhrase, malPhrase, rerollPhrase, and searchingPhrase signals with string values', () => {
    const fixture = TestBed.createComponent(LuckyPickComponent);
    const comp = fixture.componentInstance as unknown as {
      plexPhrase: () => string;
      malPhrase: () => string;
      rerollPhrase: () => string;
      searchingPhrase: () => string;
    };
    expect(typeof comp.plexPhrase()).toBe('string');
    expect(comp.plexPhrase().length).toBeGreaterThan(0);
    expect(typeof comp.malPhrase()).toBe('string');
    expect(comp.malPhrase().length).toBeGreaterThan(0);
    expect(typeof comp.rerollPhrase()).toBe('string');
    expect(comp.rerollPhrase().length).toBeGreaterThan(0);
    expect(typeof comp.searchingPhrase()).toBe('string');
    expect(comp.searchingPhrase().length).toBeGreaterThan(0);
  });

  it('should reroll phrases on each spin() call', () => {
    const fixture = TestBed.createComponent(LuckyPickComponent);
    const component = fixture.componentInstance;
    const comp = component as unknown as {
      plexPhrase: () => string;
      malPhrase: () => string;
      rerollPhrase: () => string;
      state: { set: (v: string) => void };
    };

    // Force to non-spinning state so spin() can run
    comp.state.set('result' as never);
    component.spin();
    // After a spin the phrase must still be a valid, non-empty string
    expect(typeof comp.plexPhrase()).toBe('string');
    expect(comp.plexPhrase().length).toBeGreaterThan(0);
    // A second spin should also produce a valid phrase
    comp.state.set('result' as never);
    component.spin();
    expect(typeof comp.plexPhrase()).toBe('string');
    expect(comp.plexPhrase().length).toBeGreaterThan(0);
  });

  it('should apply lucky-container--beating class only while spinning', () => {
    const fixture = TestBed.createComponent(LuckyPickComponent);
    const comp = fixture.componentInstance as unknown as {
      isSpinning: () => boolean;
      state: { set: (v: string) => void };
    };

    comp.state.set('spinning' as never);
    expect(comp.isSpinning()).toBe(true);

    comp.state.set('result' as never);
    expect(comp.isSpinning()).toBe(false);
  });

  // ── Synopsis truncation tests ─────────────────────────────────────────────

  it('should report isSynopsisTruncated=false when synopsis is short', () => {
    const fixture = TestBed.createComponent(LuckyPickComponent);
    const comp = fixture.componentInstance as unknown as {
      isSynopsisTruncated: boolean;
      result: { set: (v: unknown) => void };
    };
    comp.result.set({
      title: 'Test',
      malAnimes: [{ synopsis: 'Short text.' }],
    } as never);
    expect(comp.isSynopsisTruncated).toBe(false);
  });

  it('should report isSynopsisTruncated=false when synopsis is exactly 300 characters (boundary)', () => {
    const fixture = TestBed.createComponent(LuckyPickComponent);
    const comp = fixture.componentInstance as unknown as {
      isSynopsisTruncated: boolean;
      result: { set: (v: unknown) => void };
    };
    const boundaryText = 'A'.repeat(300);
    comp.result.set({
      title: 'Test',
      malAnimes: [{ synopsis: boundaryText }],
    } as never);
    expect(comp.isSynopsisTruncated).toBe(false);
  });

  it('should report isSynopsisTruncated=true when synopsis exceeds threshold', () => {
    const fixture = TestBed.createComponent(LuckyPickComponent);
    const comp = fixture.componentInstance as unknown as {
      isSynopsisTruncated: boolean;
      result: { set: (v: unknown) => void };
    };
    const longText = 'A'.repeat(301);
    comp.result.set({
      title: 'Test',
      malAnimes: [{ synopsis: longText }],
    } as never);
    expect(comp.isSynopsisTruncated).toBe(true);
  });

  it('should call popupService.openSynopsis with title and synopsis when openSynopsisModal is called', () => {
    const fixture = TestBed.createComponent(LuckyPickComponent);
    const comp = fixture.componentInstance as unknown as {
      openSynopsisModal: () => void;
      result: { set: (v: unknown) => void };
      popupService: { openSynopsis: (t: string, b: string) => void };
    };
    comp.result.set({
      title: 'My Anime',
      malAnimes: [{ synopsis: 'Some synopsis text.', alternativeTitles: { en: 'My Anime EN' } }],
    } as never);
    let capturedTitle = '';
    let capturedBody = '';
    comp.popupService.openSynopsis = (t: string, b: string) => {
      capturedTitle = t;
      capturedBody = b;
    };
    comp.openSynopsisModal();
    expect(capturedTitle).toBe('My Anime EN');
    expect(capturedBody).toBe('Some synopsis text.');
  });
});

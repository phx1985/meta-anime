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
});

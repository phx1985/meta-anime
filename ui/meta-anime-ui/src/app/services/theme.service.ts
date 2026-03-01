import { Injectable, signal, effect } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly STORAGE_KEY = 'meta-anime-theme';
  private readonly FOLLOW_OS_KEY = 'meta-anime-follow-os';

  readonly isDarkTheme = signal<boolean>(this.loadTheme());
  readonly followOsTheme = signal<boolean>(this.loadFollowOs());

  private readonly mediaQuery: MediaQueryList | null =
    typeof window !== 'undefined' && typeof window.matchMedia === 'function'
      ? window.matchMedia('(prefers-color-scheme: dark)')
      : null;

  private readonly osListener = (e: MediaQueryListEvent): void => {
    if (this.followOsTheme()) {
      this.isDarkTheme.set(e.matches);
      this.applyTheme(e.matches);
    }
  };

  constructor() {
    effect(() => {
      this.applyTheme(this.isDarkTheme());
    });

    // Start listening to OS theme if followOsTheme was persisted
    if (this.followOsTheme()) {
      this.mediaQuery?.addEventListener('change', this.osListener);
    }
  }

  ngOnDestroy(): void {
    // Called on application shutdown; remove MediaQueryList listener
    this.mediaQuery?.removeEventListener('change', this.osListener);
  }

  toggleTheme(): void {
    // Manual toggle disables OS follow
    if (this.followOsTheme()) {
      this.setFollowOsTheme(false);
    }
    this.isDarkTheme.update((v) => !v);
    localStorage.setItem(this.STORAGE_KEY, this.isDarkTheme() ? 'dark' : 'light');
  }

  setDarkTheme(dark: boolean): void {
    // Manual set disables OS follow
    if (this.followOsTheme()) {
      this.setFollowOsTheme(false);
    }
    this.isDarkTheme.set(dark);
    localStorage.setItem(this.STORAGE_KEY, dark ? 'dark' : 'light');
  }

  setFollowOsTheme(val: boolean): void {
    this.followOsTheme.set(val);
    localStorage.setItem(this.FOLLOW_OS_KEY, val ? 'true' : 'false');
    if (val) {
      // Immediately sync to OS
      const prefersDark = this.mediaQuery?.matches ?? false;
      this.isDarkTheme.set(prefersDark);
      localStorage.setItem(this.STORAGE_KEY, prefersDark ? 'dark' : 'light');
      this.mediaQuery?.addEventListener('change', this.osListener);
    } else {
      this.mediaQuery?.removeEventListener('change', this.osListener);
    }
  }

  private loadFollowOs(): boolean {
    return typeof localStorage !== 'undefined'
      ? localStorage.getItem(this.FOLLOW_OS_KEY) === 'true'
      : false;
  }

  private loadTheme(): boolean {
    const stored = localStorage.getItem(this.STORAGE_KEY);
    if (stored) return stored === 'dark';
    return typeof window !== 'undefined' && typeof window.matchMedia === 'function'
      ? window.matchMedia('(prefers-color-scheme: dark)').matches
      : false;
  }

  private applyTheme(dark: boolean): void {
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
  }
}

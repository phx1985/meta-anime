import { Injectable, signal, effect } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly STORAGE_KEY = 'meta-anime-theme';
  readonly isDarkTheme = signal<boolean>(this.loadTheme());

  constructor() {
    effect(() => {
      this.applyTheme(this.isDarkTheme());
    });
  }

  toggleTheme(): void {
    this.isDarkTheme.update(v => !v);
    localStorage.setItem(this.STORAGE_KEY, this.isDarkTheme() ? 'dark' : 'light');
  }

  private loadTheme(): boolean {
    const stored = localStorage.getItem(this.STORAGE_KEY);
    if (stored) return stored === 'dark';
    return window.matchMedia('(prefers-color-scheme: dark)').matches;
  }

  private applyTheme(dark: boolean): void {
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
  }
}

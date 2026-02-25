import { Injectable, signal, effect } from '@angular/core';

const AUTO_APPLY_KEY = 'meta-anime:autoApplyLastFilters';
const LAST_FILTERS_KEY = 'meta-anime:lastUsedFilters';

@Injectable({ providedIn: 'root' })
export class UserPreferencesService {
  /** When true, the last used anime search filters are restored automatically on page load. */
  readonly autoApplyLastFilters = signal<boolean>(this.loadBool(AUTO_APPLY_KEY));

  constructor() {
    effect(() => {
      localStorage.setItem(AUTO_APPLY_KEY, this.autoApplyLastFilters() ? 'true' : 'false');
    });
  }

  setAutoApplyLastFilters(value: boolean): void {
    this.autoApplyLastFilters.set(value);
  }

  saveLastFilters(filters: string): void {
    localStorage.setItem(LAST_FILTERS_KEY, filters);
  }

  loadLastFilters(): string | null {
    return localStorage.getItem(LAST_FILTERS_KEY);
  }

  private loadBool(key: string): boolean {
    return typeof localStorage !== 'undefined'
      ? localStorage.getItem(key) === 'true'
      : false;
  }
}

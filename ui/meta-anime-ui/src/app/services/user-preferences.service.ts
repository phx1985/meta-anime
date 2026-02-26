import { Injectable, signal, effect } from '@angular/core';
import { StorageKeys } from '../constants/storage-keys';

const AUTO_APPLY_KEY = StorageKeys.autoApplyLastFilters;
const LAST_FILTERS_KEY = StorageKeys.lastUsedFilters;
const LUCKY_ONLY_FINISHED_KEY = StorageKeys.luckyOnlyFinished;
const LUCKY_ONLY_PLAN_TO_WATCH_KEY = StorageKeys.luckyOnlyPlanToWatch;
const LUCKY_ONLY_WITH_FILES_KEY = StorageKeys.luckyOnlyWithFiles;

@Injectable({ providedIn: 'root' })
export class UserPreferencesService {
  /** When true, the last used anime search filters are restored automatically on page load. */
  readonly autoApplyLastFilters = signal<boolean>(this.loadBool(AUTO_APPLY_KEY));

  // ── Lucky Pick preferences ─────────────────────────────────────────────────
  /** When true, "I'm Feeling Lucky" only selects from finished anime. Default: false */
  readonly luckyOnlyFinished = signal<boolean>(this.loadBool(LUCKY_ONLY_FINISHED_KEY, false));
  /** When true, "I'm Feeling Lucky" only selects from "Plan to Watch" entries. Default: true */
  readonly luckyOnlyPlanToWatch = signal<boolean>(
    this.loadBool(LUCKY_ONLY_PLAN_TO_WATCH_KEY, true),
  );
  /** When true, "I'm Feeling Lucky" only selects anime that have local files. Default: true */
  readonly luckyOnlyWithFiles = signal<boolean>(this.loadBool(LUCKY_ONLY_WITH_FILES_KEY, true));

  constructor() {
    effect(() => {
      localStorage.setItem(AUTO_APPLY_KEY, this.autoApplyLastFilters() ? 'true' : 'false');
    });
    effect(() => {
      localStorage.setItem(LUCKY_ONLY_FINISHED_KEY, this.luckyOnlyFinished() ? 'true' : 'false');
    });
    effect(() => {
      localStorage.setItem(
        LUCKY_ONLY_PLAN_TO_WATCH_KEY,
        this.luckyOnlyPlanToWatch() ? 'true' : 'false',
      );
    });
    effect(() => {
      localStorage.setItem(
        LUCKY_ONLY_WITH_FILES_KEY,
        this.luckyOnlyWithFiles() ? 'true' : 'false',
      );
    });
  }

  setAutoApplyLastFilters(value: boolean): void {
    this.autoApplyLastFilters.set(value);
  }

  setLuckyOnlyFinished(value: boolean): void {
    this.luckyOnlyFinished.set(value);
  }

  setLuckyOnlyPlanToWatch(value: boolean): void {
    this.luckyOnlyPlanToWatch.set(value);
  }

  setLuckyOnlyWithFiles(value: boolean): void {
    this.luckyOnlyWithFiles.set(value);
  }

  saveLastFilters(filters: string): void {
    localStorage.setItem(LAST_FILTERS_KEY, filters);
  }

  loadLastFilters(): string | null {
    return localStorage.getItem(LAST_FILTERS_KEY);
  }

  private loadBool(key: string, defaultValue = false): boolean {
    if (typeof localStorage === 'undefined') return defaultValue;
    const stored = localStorage.getItem(key);
    if (stored === null) return defaultValue;
    return stored === 'true';
  }
}

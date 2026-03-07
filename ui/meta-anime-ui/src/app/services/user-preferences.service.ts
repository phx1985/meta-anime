import { Injectable, signal, effect, inject } from '@angular/core';
import { StorageKeys } from '../constants/storage-keys';
import { PropertyBagService } from './property-bag.service';
import { firstValueFrom } from 'rxjs';

const AUTO_APPLY_KEY = StorageKeys.autoApplyLastFilters;
const LAST_FILTERS_KEY = StorageKeys.lastUsedFilters;
const LAST_SEASONAL_FILTERS_KEY = StorageKeys.seasonalLastUsedFilters;
const LUCKY_ONLY_FINISHED_KEY = StorageKeys.luckyOnlyFinished;
const LUCKY_ONLY_PLAN_TO_WATCH_KEY = StorageKeys.luckyOnlyPlanToWatch;
const LUCKY_ONLY_WITH_FILES_KEY = StorageKeys.luckyOnlyWithFiles;
const WELCOME_SHOWN_KEY = StorageKeys.welcomeShown;

@Injectable({ providedIn: 'root' })
export class UserPreferencesService {
  private propertyBag = inject(PropertyBagService);

  /** When true, the last used anime search filters are restored automatically on page load. */
  readonly autoApplyLastFilters = signal<boolean>(false);

  // ── Lucky Pick preferences ─────────────────────────────────────────────────
  /** When true, "I'm Feeling Lucky" only selects from finished anime. Default: false */
  readonly luckyOnlyFinished = signal<boolean>(false);
  /** When true, "I'm Feeling Lucky" only selects from "Plan to Watch" entries. Default: true */
  readonly luckyOnlyPlanToWatch = signal<boolean>(true);
  /** When true, "I'm Feeling Lucky" only selects anime that have local files. Default: true */
  readonly luckyOnlyWithFiles = signal<boolean>(true);

  private _lastFilters: string | null = null;
  private _lastSeasonalFilters: string | null = null;
  /**
   * Tri-state: null = load() not yet completed or failed (don't show welcome),
   * false = loaded & key absent (show welcome), true = loaded & key present (don't show).
   */
  private _welcomeShown: boolean | null = null;
  /** True only after load() completes successfully — guards writes against startup races. */
  private _loaded = false;

  constructor() {
    // We no longer use effects to auto-save to localStorage.
    // Instead, we explicitly call save() when values change.
  }

  async load(): Promise<void> {
    try {
      const allProps = await firstValueFrom(this.propertyBag.getAll());
      const map = new Map(allProps.map((p) => [p.key, p.value]));

      // Helper to load boolean with migration
      const loadBool = (key: string, defaultVal: boolean, signalToSet: any) => {
        if (map.has(key)) {
          signalToSet.set(map.get(key) === 'true');
        } else {
          // Migration: Check localStorage
          const local = localStorage.getItem(key);
          if (local !== null) {
            const val = local === 'true';
            signalToSet.set(val);
            // Upload to server
            this.propertyBag.upsert(key, { value: local }).subscribe();
            localStorage.removeItem(key);
          } else {
            signalToSet.set(defaultVal);
          }
        }
      };

      loadBool(AUTO_APPLY_KEY, true, this.autoApplyLastFilters);
      loadBool(LUCKY_ONLY_FINISHED_KEY, false, this.luckyOnlyFinished);
      loadBool(LUCKY_ONLY_PLAN_TO_WATCH_KEY, true, this.luckyOnlyPlanToWatch);
      loadBool(LUCKY_ONLY_WITH_FILES_KEY, true, this.luckyOnlyWithFiles);

      // Load last filters string
      if (map.has(LAST_FILTERS_KEY)) {
        this._lastFilters = map.get(LAST_FILTERS_KEY)!;
      } else {
        const local = localStorage.getItem(LAST_FILTERS_KEY);
        if (local) {
          this._lastFilters = local;
          this.propertyBag.upsert(LAST_FILTERS_KEY, { value: local }).subscribe();
          localStorage.removeItem(LAST_FILTERS_KEY);
        }
      }

      // Load last seasonal filters string
      if (map.has(LAST_SEASONAL_FILTERS_KEY)) {
        this._lastSeasonalFilters = map.get(LAST_SEASONAL_FILTERS_KEY)!;
      } else {
        const local = localStorage.getItem(LAST_SEASONAL_FILTERS_KEY);
        if (local) {
          this._lastSeasonalFilters = local;
          this.propertyBag.upsert(LAST_SEASONAL_FILTERS_KEY, { value: local }).subscribe();
          localStorage.removeItem(LAST_SEASONAL_FILTERS_KEY);
        }
      }

      this._welcomeShown = map.has(WELCOME_SHOWN_KEY);
      this._loaded = true;

    } catch (err) {
      console.error('Failed to load user preferences from server', err);
      // Fallback to defaults or local storage if offline?
      // For now, we assume server is available or we fail gracefully.
    }
  }

  isWelcomeShown(): boolean | null {
    return this._welcomeShown;
  }

  markWelcomeShown(): void {
    this._welcomeShown = true;
    this.propertyBag.upsert(WELCOME_SHOWN_KEY, { value: 'true' }).subscribe({
      error: (e) => console.error('[UserPreferencesService] Failed to mark welcome as shown', e),
    });
  }

  setAutoApplyLastFilters(value: boolean): void {
    this.autoApplyLastFilters.set(value);
    this.saveBool(AUTO_APPLY_KEY, value);
  }

  setLuckyOnlyFinished(value: boolean): void {
    this.luckyOnlyFinished.set(value);
    this.saveBool(LUCKY_ONLY_FINISHED_KEY, value);
  }

  setLuckyOnlyPlanToWatch(value: boolean): void {
    this.luckyOnlyPlanToWatch.set(value);
    this.saveBool(LUCKY_ONLY_PLAN_TO_WATCH_KEY, value);
  }

  setLuckyOnlyWithFiles(value: boolean): void {
    this.luckyOnlyWithFiles.set(value);
    this.saveBool(LUCKY_ONLY_WITH_FILES_KEY, value);
  }

  saveLastFilters(filters: string): void {
    if (!this._loaded) return;
    this._lastFilters = filters;
    this.propertyBag.upsert(LAST_FILTERS_KEY, { value: filters }).subscribe({
      error: (e) => console.error('[UserPreferencesService] Failed to save last filters', e),
    });
  }

  loadLastFilters(): string | null {
    return this._lastFilters;
  }

  saveLastSeasonalFilters(filters: string): void {
    if (!this._loaded) return;
    this._lastSeasonalFilters = filters;
    this.propertyBag.upsert(LAST_SEASONAL_FILTERS_KEY, { value: filters }).subscribe({
      error: (e) => console.error('[UserPreferencesService] Failed to save last seasonal filters', e),
    });
  }

  loadLastSeasonalFilters(): string | null {
    return this._lastSeasonalFilters;
  }

  private saveBool(key: string, value: boolean): void {
    if (!this._loaded) return;
    this.propertyBag.upsert(key, { value: value ? 'true' : 'false' }).subscribe({
      error: (e) => console.error('[UserPreferencesService] Failed to save preference', key, e),
    });
  }
}

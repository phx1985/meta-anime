import { Injectable, signal, inject, computed } from '@angular/core';
import { FilterPreset, SeasonalFilterPreset } from '../models/anime.models';
import { StorageKeys } from '../constants/storage-keys';
import { PropertyBagService } from './property-bag.service';
import { firstValueFrom } from 'rxjs';

const PRESETS_KEY = StorageKeys.filterPresets;
const SEASONAL_PRESETS_KEY = StorageKeys.seasonalFilterPresets;

/** Built-in filter suggestions shown to every user. */
export const BUILT_IN_PRESETS: FilterPreset[] = [
  {
    id: 'builtin-currently-airing',
    labelKey: 'animeList.presets.builtIn.currentlyAiring',
    isBuiltIn: true,
    filters: { airingStatus: ['currently_airing'], sortBy: 'malscore', sortOrder: 'desc' },
  },
  {
    id: 'builtin-top-rated',
    labelKey: 'animeList.presets.builtIn.topRated',
    isBuiltIn: true,
    filters: { sortBy: 'malscore', sortOrder: 'desc' },
  },
  {
    id: 'builtin-watching',
    labelKey: 'animeList.presets.builtIn.watching',
    isBuiltIn: true,
    filters: { watchState: ['watching'] },
  },
  {
    id: 'builtin-plan-to-watch',
    labelKey: 'animeList.presets.builtIn.planToWatch',
    isBuiltIn: true,
    filters: { watchState: ['plan_to_watch'] },
  },
];

@Injectable({ providedIn: 'root' })
export class FilterPresetService {
  private propertyBag = inject(PropertyBagService);

  readonly builtInPresets: FilterPreset[] = BUILT_IN_PRESETS;

  // Reactive signals for presets
  private readonly _savedPresets = signal<FilterPreset[]>([]);
  readonly savedPresets = this._savedPresets.asReadonly();

  private readonly _seasonalPresets = signal<SeasonalFilterPreset[]>([]);
  readonly seasonalPresets = this._seasonalPresets.asReadonly();

  /** True only after load() completes successfully — guards writes against startup races. */
  private _loaded = false;

  async load(): Promise<void> {
    try {
      const allProps = await firstValueFrom(this.propertyBag.getAll());
      const map = new Map(allProps.map((p) => [p.key, p.value]));

      // ── Load Regular Presets ───────────────────────────────────────────────
      if (map.has(PRESETS_KEY)) {
        try {
          const loaded = JSON.parse(map.get(PRESETS_KEY)!) as FilterPreset[];
          this._savedPresets.set(loaded);
        } catch (e) {
          console.error('Failed to parse filter presets from server', e);
        }
      } else {
        // Migration
        const local = localStorage.getItem(PRESETS_KEY);
        if (local) {
          try {
            const loaded = JSON.parse(local) as FilterPreset[];
            this._savedPresets.set(loaded);
            // Upload
            this.propertyBag.upsert(PRESETS_KEY, { value: local }).subscribe();
            localStorage.removeItem(PRESETS_KEY);
          } catch {}
        }
      }

      // ── Load Seasonal Presets ──────────────────────────────────────────────
      if (map.has(SEASONAL_PRESETS_KEY)) {
        try {
          const loaded = JSON.parse(map.get(SEASONAL_PRESETS_KEY)!) as SeasonalFilterPreset[];
          this._seasonalPresets.set(loaded);
        } catch (e) {
          console.error('Failed to parse seasonal presets from server', e);
        }
      } else {
        // Migration
        const local = localStorage.getItem(SEASONAL_PRESETS_KEY);
        if (local) {
          try {
            const loaded = JSON.parse(local) as SeasonalFilterPreset[];
            this._seasonalPresets.set(loaded);
            // Upload
            this.propertyBag.upsert(SEASONAL_PRESETS_KEY, { value: local }).subscribe();
            localStorage.removeItem(SEASONAL_PRESETS_KEY);
          } catch {}
        }
      }
    } catch (err) {
      console.error('Failed to load filter presets from server', err);
    }

    this._loaded = true;
  }

  getSavedPresets(): FilterPreset[] {
    return this._savedPresets();
  }

  savePreset(preset: FilterPreset): void {
    if (!this._loaded) return;
    const current = this._savedPresets();
    const existingIndex = current.findIndex((p) => p.id === preset.id);
    let updated: FilterPreset[];

    if (existingIndex >= 0) {
      updated = [...current];
      updated[existingIndex] = preset;
    } else {
      updated = [...current, preset];
    }
    
    this._savedPresets.set(updated);
    this.propertyBag.upsert(PRESETS_KEY, { value: JSON.stringify(updated) }).subscribe({
      error: (e) => console.error('[FilterPresetService] Failed to save preset to server', e),
    });
  }

  deletePreset(id: string): void {
    if (!this._loaded) return;
    const updated = this._savedPresets().filter((p) => p.id !== id && !p.isBuiltIn);
    this._savedPresets.set(updated);
    this.propertyBag.upsert(PRESETS_KEY, { value: JSON.stringify(updated) }).subscribe({
      error: (e) => console.error('[FilterPresetService] Failed to delete preset on server', e),
    });
  }

  // ── Seasonal filter presets ────────────────────────────────────────────────

  getSeasonalPresets(): SeasonalFilterPreset[] {
    return this._seasonalPresets();
  }

  saveSeasonalPreset(preset: SeasonalFilterPreset): void {
    if (!this._loaded) return;
    const current = this._seasonalPresets();
    const existingIndex = current.findIndex((p) => p.id === preset.id);
    let updated: SeasonalFilterPreset[];

    if (existingIndex >= 0) {
      updated = [...current];
      updated[existingIndex] = preset;
    } else {
      updated = [...current, preset];
    }

    this._seasonalPresets.set(updated);
    this.propertyBag.upsert(SEASONAL_PRESETS_KEY, { value: JSON.stringify(updated) }).subscribe({
      error: (e) => console.error('[FilterPresetService] Failed to save seasonal preset to server', e),
    });
  }

  deleteSeasonalPreset(id: string): void {
    if (!this._loaded) return;
    const updated = this._seasonalPresets().filter((p) => p.id !== id);
    this._seasonalPresets.set(updated);
    this.propertyBag.upsert(SEASONAL_PRESETS_KEY, { value: JSON.stringify(updated) }).subscribe({
      error: (e) => console.error('[FilterPresetService] Failed to delete seasonal preset on server', e),
    });
  }
}

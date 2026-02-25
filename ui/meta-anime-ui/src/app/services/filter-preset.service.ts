import { Injectable } from '@angular/core';
import { FilterPreset, SeasonalFilterPreset } from '../models/anime.models';

const PRESETS_KEY = 'meta-anime:filterPresets';
const SEASONAL_PRESETS_KEY = 'meta-anime:seasonalFilterPresets';

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
  readonly builtInPresets: FilterPreset[] = BUILT_IN_PRESETS;

  getSavedPresets(): FilterPreset[] {
    try {
      const raw = localStorage.getItem(PRESETS_KEY);
      return raw ? (JSON.parse(raw) as FilterPreset[]) : [];
    } catch {
      return [];
    }
  }

  savePreset(preset: FilterPreset): void {
    const presets = this.getSavedPresets();
    presets.push(preset);
    localStorage.setItem(PRESETS_KEY, JSON.stringify(presets));
  }

  deletePreset(id: string): void {
    // Only user-saved presets can be deleted (built-in presets are never in localStorage)
    const presets = this.getSavedPresets().filter((p) => p.id !== id && !p.isBuiltIn);
    localStorage.setItem(PRESETS_KEY, JSON.stringify(presets));
  }

  // ── Seasonal filter presets ────────────────────────────────────────────────

  getSeasonalPresets(): SeasonalFilterPreset[] {
    try {
      const raw = localStorage.getItem(SEASONAL_PRESETS_KEY);
      return raw ? (JSON.parse(raw) as SeasonalFilterPreset[]) : [];
    } catch {
      return [];
    }
  }

  saveSeasonalPreset(preset: SeasonalFilterPreset): void {
    const presets = this.getSeasonalPresets().filter((p) => p.id !== preset.id);
    presets.push(preset);
    localStorage.setItem(SEASONAL_PRESETS_KEY, JSON.stringify(presets));
  }

  deleteSeasonalPreset(id: string): void {
    const presets = this.getSeasonalPresets().filter((p) => p.id !== id);
    localStorage.setItem(SEASONAL_PRESETS_KEY, JSON.stringify(presets));
  }
}

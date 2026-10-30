import { Injectable, signal, WritableSignal } from '@angular/core';
import { LibrarySeriesRow } from '../models/library-analysis.models';
import {
  BotKunActionPayload,
  defaultFilterState,
  deriveSeriesStatus,
  LibraryFilterState,
} from '../models/library-intel.models';

/**
 * Shared signal store for the Ultra Library Buddy.
 * Holds the filter state (driven by the analysis table, KPI quick-chips and
 * Bot-kun), the enriched rows (needed for Bot-kun "Open Series"), and the
 * current drawer selection so any surface can open a series detail.
 */
@Injectable({ providedIn: 'root' })
export class LibraryIntelStore {
  /** Complete filter snapshot — analysis-table binds to this instead of local state. */
  readonly filters: WritableSignal<LibraryFilterState> = signal(defaultFilterState());

  /** Id of the active quick-filter preset (highlight ring; toggled off on second click). */
  readonly activePresetId: WritableSignal<string | null> = signal(null);

  /** Rows currently in the table (post-enrichment); kept for Bot-kun lookups. */
  readonly rows: WritableSignal<LibrarySeriesRow[]> = signal([]);

  /** Drawer selection — set by table row clicks, KPI leaderboard clicks, or Bot-kun. */
  readonly selectedRow: WritableSignal<LibrarySeriesRow | null> = signal<LibrarySeriesRow | null>(null);

  /** Applies a partial filter patch (used by KPI chips + Bot-kun) and resets paging. */
  applyFilterPatch(patch: Partial<LibraryFilterState>): void {
    this.filters.update(f => ({
      ...f,
      ...patch,
      codecThreshold: patch.codecThreshold ?? f.codecThreshold,
      page: 1,
    }));
    if (typeof window !== 'undefined') {
      document.querySelector('.la-container')?.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
    }
  }

  /** Clears every filter back to defaults. */
  resetFilters(): void {
    const keep = { sortCol: this.filters().sortCol, sortDir: this.filters().sortDir, pageSize: this.filters().pageSize };
    this.filters.set({ ...defaultFilterState(), ...keep });
    this.activePresetId.set(null);
  }

  /**
   * Reset-then-apply: clears ALL filters first so stale toggles (resolution,
   * bit depth, split-codecs, …) can never merge into a preset's criteria.
   */
  applyPresetPatch(patch: Partial<LibraryFilterState>): void {
    const keep = { sortCol: this.filters().sortCol, sortDir: this.filters().sortDir, pageSize: this.filters().pageSize };
    this.filters.set({ ...defaultFilterState(), ...keep, ...patch, page: 1 });
    if (typeof window !== 'undefined') {
      document.querySelector('.la-container')?.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
    }
  }

  /**
   * Quick-filter preset toggle: first click = clear + apply and highlight,
   * second click on the same preset = clear and deactivate.
   */
  togglePreset(id: string, patch: Partial<LibraryFilterState>): void {
    if (this.activePresetId() === id) {
      this.resetFilters();
      return;
    }
    this.applyPresetPatch(patch);
    this.activePresetId.set(id);
  }

  /** Opens the detail drawer for a series (by row or Sonarr ID). */
  openSeries(target: LibrarySeriesRow | number): void {
    const row = typeof target === 'number'
      ? this.rows().find(r => r.sonarrId === target) ?? null
      : target;
    this.selectedRow.set(row);
  }

  closeSeries(): void {
    this.selectedRow.set(null);
  }

  /** Single dispatch point for Bot-kun [act:…] payloads. */
  applyBotKunAction(action: BotKunActionPayload): void {
    switch (action.type) {
      case 'applyFilter':
        // Reset-then-apply — a Bot-kun action defines the complete filter set.
        this.applyPresetPatch(action.filter);
        break;
      case 'openSeries':
        this.openSeries(action.sonarrId);
        break;
    }
  }

  /** Season-aware status helper (re-exported for template consumers). */
  readonly statusOf = deriveSeriesStatus;
}

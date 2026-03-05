import { Injectable, WritableSignal, signal } from '@angular/core';

export interface MalQuickUpdateContext {
  malId: string;
  title: string;
  coverImageUrl?: string;
  malUrl?: string;
  currentStatus?: string;
  currentEpisodes?: number;
  currentScore?: number;
  totalEpisodes?: number;
  /** MAL series airing status — used to auto-fill episodes on Completed */
  seriesStatus?: string;
  /** Called on successful save so the originating card can update its local state immediately. */
  onSaveSuccess?: (status: string, episodes: number, score: number) => void;
}

/**
 * Central service for all application modal/popup state.
 * Components must not manage their own modal signals; delegate here instead.
 */
@Injectable({ providedIn: 'root' })
export class PopupService {
  /** Duration (ms) that matches the CSS exit-animation duration for all modals. */
  static readonly MODAL_LEAVE_MS = 200;

  // ── Child-component overlay flag ──────────────────────────────────────────────

  /**
   * Set to `true` by routed child components (anime-list, seasonal) whenever
   * their local filter or presets bottom-sheet is open on mobile.  The app-root
   * watches this to retract the floating bottom nav bar.
   */
  readonly childOverlayOpen = signal(false);

  /**
   * Set to `true` by the seasonal-card component when its "Add to Providers"
   * modal is open.  Included in the global anyOverlayOpen check so the bottom
   * nav bar is correctly hidden while the modal is visible.
   */
  readonly addProviderModalOpen = signal(false);

  /**
   * Set to `true` by the franchise constellation component when the full-screen
   * overlay is open.  Included in the global anyOverlayOpen check.
   */
  readonly franchiseConstellationOpen = signal(false);

  // ── Disabled-provider interception modal ─────────────────────────────────────

  /** Which provider is currently blocked; `null` means the modal is closed. */
  readonly disabledProvider = signal<'plex' | 'sonarr' | null>(null);
  /** `true` while the exit animation is playing (CSS `--leaving` modifier). */
  readonly disabledProviderLeaving = signal(false);

  openDisabledProvider(provider: 'plex' | 'sonarr'): void {
    this.disabledProvider.set(provider);
    this.disabledProviderLeaving.set(false);
  }

  dismissDisabledProvider(): void {
    this._animateOut(this.disabledProviderLeaving, () => this.disabledProvider.set(null));
  }

  // ── Sync-prompt modal ─────────────────────────────────────────────────────────

  /** Provider name when sync prompt is showing; `null` means the modal is closed. */
  readonly syncPromptProvider = signal<string | null>(null);
  /** `true` while the exit animation is playing. */
  readonly syncPromptLeaving = signal(false);

  openSyncPrompt(provider: string): void {
    this.syncPromptProvider.set(provider);
    this.syncPromptLeaving.set(false);
  }

  dismissSyncPrompt(): void {
    this._animateOut(this.syncPromptLeaving, () => this.syncPromptProvider.set(null));
  }

  // ── Synopsis modal ────────────────────────────────────────────────────────────

  /** Anime title shown as the modal header; `null` means the modal is closed. */
  readonly synopsisTitle = signal<string | null>(null);
  /** Full synopsis text shown in the scrollable modal body. */
  readonly synopsisBody = signal<string>('');
  /** `true` while the exit animation is playing. */
  readonly synopsisLeaving = signal(false);

  openSynopsis(title: string, body: string): void {
    this.synopsisTitle.set(title);
    this.synopsisBody.set(body);
    this.synopsisLeaving.set(false);
  }

  dismissSynopsis(): void {
    this._animateOut(this.synopsisLeaving, () => this.synopsisTitle.set(null));
  }

  // ── MAL Quick-Update modal ────────────────────────────────────────────────────

  /** Context of the anime being quick-updated; `null` means the modal is closed. */
  readonly malQuickUpdate = signal<MalQuickUpdateContext | null>(null);
  /** `true` while the exit animation is playing. */
  readonly malQuickUpdateLeaving = signal(false);
  /** `true` while the MAL quick-update API call is in flight. */
  readonly malQuickUpdateSaving = signal(false);

  openMalQuickUpdate(ctx: MalQuickUpdateContext): void {
    this.malQuickUpdate.set(ctx);
    this.malQuickUpdateLeaving.set(false);
  }

  dismissMalQuickUpdate(): void {
    this.malQuickUpdateSaving.set(false);
    this._animateOut(this.malQuickUpdateLeaving, () => this.malQuickUpdate.set(null));
  }

  // ── Shared helpers ────────────────────────────────────────────────────────────

  /**
   * Sets the `leaving` signal, waits for the exit-animation to complete, then
   * invokes `onDone` (which should null-out the modal's primary signal so `@if`
   * removes the DOM node).
   */
  private _animateOut(leaving: WritableSignal<boolean>, onDone: () => void): void {
    leaving.set(true);
    setTimeout(() => {
      onDone();
      leaving.set(false);
    }, PopupService.MODAL_LEAVE_MS);
  }
}

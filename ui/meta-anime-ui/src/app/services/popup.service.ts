import { Injectable, WritableSignal, signal } from '@angular/core';

/**
 * Central service for all application modal/popup state.
 * Components must not manage their own modal signals; delegate here instead.
 */
@Injectable({ providedIn: 'root' })
export class PopupService {
  /** Duration (ms) that matches the CSS exit-animation duration for all modals. */
  static readonly MODAL_LEAVE_MS = 200;

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

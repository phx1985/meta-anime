import { Injectable, signal } from '@angular/core';

/** One 3-second blush burst: where it started and which burst it is. */
export interface BlushBurst {
  /** Monotonic id — lets the overlay re-randomise its hearts per burst. */
  id: number;
  /** Viewport coordinates the hearts rise from (toggle position when known). */
  originX: number;
  originY: number;
}

/**
 * NsfwEasterEggService
 *
 * The "Show NSFW Content" easter egg: enabling that toggle (false → true) fires a
 * 3-second blush overlay with floating hearts and a short voice clip.
 *
 * Owned as a root singleton so the overlay can be rendered once at the app root —
 * NOT inside the settings card, where a transformed/filtered ancestor would trap
 * `position: fixed` and clip the animation.
 */
@Injectable({ providedIn: 'root' })
export class NsfwEasterEggService {
  /** Swap the file in `src/assets/sounds/` to change the clip. */
  private static readonly SOUND_SRC = 'assets/sounds/fairy-tail-wow.mp3';
  /** Total overlay lifetime; the app root unmounts the overlay when this clears. */
  static readonly BURST_MS = 3000;

  private readonly _burst = signal<BlushBurst | null>(null);
  /** Current burst, or null when nothing is playing. */
  readonly burst = this._burst.asReadonly();

  private audio: HTMLAudioElement | null = null;
  private burstTimer: ReturnType<typeof setTimeout> | null = null;
  private burstSeq = 0;

  /**
   * Fires the easter egg. Safe to call repeatedly — a new burst replaces the running one
   * (and restarts the clip from the beginning instead of stacking playbacks).
   */
  celebrate(origin?: { x: number; y: number }): void {
    this.playSound();

    // Default to the middle of the viewport when the trigger element is unknown.
    const x = origin?.x ?? window.innerWidth / 2;
    const y = origin?.y ?? window.innerHeight * 0.45;

    // Keep the spawn point on screen so hearts are never born outside the visible area.
    const originX = Math.min(Math.max(x, 24), Math.max(window.innerWidth - 24, 24));
    const originY = Math.min(Math.max(y, 24), Math.max(window.innerHeight - 24, 24));

    if (this.burstTimer !== null) clearTimeout(this.burstTimer);
    this._burst.set({ id: ++this.burstSeq, originX, originY });
    this.burstTimer = setTimeout(() => {
      this._burst.set(null);
      this.burstTimer = null;
    }, NsfwEasterEggService.BURST_MS);
  }

  private playSound(): void {
    try {
      this.audio ??= new Audio(NsfwEasterEggService.SOUND_SRC);
      // Restart from the beginning so rapid toggling never plays a truncated tail.
      this.audio.currentTime = 0;
      this.audio.volume = 0.7;
      // Browsers may block autoplay without a user gesture — the blush still plays.
      void this.audio.play().catch(() => undefined);
    } catch {
      // Audio is decorative: any failure (unsupported codec, blocked autoplay) is silent.
    }
  }
}

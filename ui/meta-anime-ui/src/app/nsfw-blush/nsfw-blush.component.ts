import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { BlushBurst, NsfwEasterEggService } from '../services/nsfw-easter-egg.service';

/** A single floating heart, pre-computed into an inline style string. */
interface BlushHeart {
  key: string;
  /** Inline CSS: position + the random custom properties the keyframes read. */
  style: string;
}

/**
 * NsfwBlushComponent
 *
 * The visible half of the "Show NSFW Content" easter egg: a 3-second, non-interactive
 * blush vignette with pastel hearts rising from the toggle. Rendered once at the app
 * root so `position: fixed` always resolves against the viewport.
 *
 * Lifecycle: fade in 300 ms, hold, fade out 600 ms (started 400 ms before the 3 s
 * lifetime ends), then the service clears the burst and the host is unmounted by @if.
 */
@Component({
  selector: 'app-nsfw-blush',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './nsfw-blush.component.html',
  styleUrl: './nsfw-blush.component.scss',
})
export class NsfwBlushComponent {
  private readonly destroyRef = inject(DestroyRef);

  /** Active burst; null means "not visible" (the owner unmounts us). */
  readonly burst = input.required<BlushBurst | null>();

  /** Hearts for the current burst — re-randomised on every new burst id. */
  readonly hearts = computed<BlushHeart[]>(() => {
    const burst = this.burst();
    return burst ? NsfwBlushComponent.spawnHearts(burst) : [];
  });

  /** True during the 600 ms exit fade. */
  readonly fadingOut = signal(false);

  private fadeTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    // Each new burst restarts the fade-out countdown (and clears the previous one).
    effect(() => {
      const burst = this.burst();
      if (!burst) return;

      if (this.fadeTimer !== null) clearTimeout(this.fadeTimer);
      this.fadingOut.set(false);

      const fadeAt = NsfwEasterEggService.BURST_MS - 600;
      this.fadeTimer = setTimeout(() => this.fadingOut.set(true), fadeAt);
    });

    this.destroyRef.onDestroy(() => {
      if (this.fadeTimer !== null) clearTimeout(this.fadeTimer);
    });
  }

  /** Where a heart starts: jittered around the burst origin. */
  trackStyle(heart: BlushHeart): string {
    return heart.style;
  }

  /**
   * 18–25 hearts (fewer on narrow viewports so mobiles keep the burst readable and can
   * never create horizontal scroll: everything lives inside an `overflow: hidden` layer).
   */
  private static spawnHearts(burst: BlushBurst): BlushHeart[] {
    const narrow = window.innerWidth < 640;
    const count = narrow ? 14 : 22;

    const palette = ['#ff69b4', '#ff1493', '#ffb6c1', '#ff8fb1', '#ff5fa2'];
    return Array.from({ length: count }, (_, i) => {
      // Fan the spawn points out sideways so hearts do not leave as a single column.
      const spread = narrow ? 70 : 130;
      const startX = burst.originX + (Math.random() - 0.5) * spread;
      const startY = burst.originY + (Math.random() - 0.5) * 28;

      const rise = -(140 + Math.random() * 260);          // upward travel (px)
      const drift = (Math.random() - 0.5) * 180;          // sideways drift (px)
      const scale = 0.65 + Math.random() * 0.85;
      const rotate = (Math.random() - 0.5) * 60;          // deg of wobble
      const size = 14 + Math.round(Math.random() * 14);   // 14–28 px
      const delay = Math.round(Math.random() * 420);      // ms
      const duration = 2200 + Math.round(Math.random() * 700); // ms — ends inside the 3 s
      const color = palette[i % palette.length];

      const style = [
        `left:${startX.toFixed(1)}px`,
        `top:${startY.toFixed(1)}px`,
        `--heart-size:${size}px`,
        `--heart-color:${color}`,
        `--rise:${rise.toFixed(1)}px`,
        `--drift:${drift.toFixed(1)}px`,
        `--heart-scale:${scale.toFixed(2)}`,
        `--wobble:${rotate.toFixed(1)}deg`,
        `--dur:${duration}ms`,
        `--delay:${delay}ms`,
      ].join(';');

      return { key: `${burst.id}-${i}`, style };
    });
  }
}

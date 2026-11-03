import { Directive, effect, ElementRef, inject, input } from '@angular/core';

/**
 * CountUpDirective
 *
 * Animates a numeric counter from its previous displayed value to the new
 * [countUp] input using requestAnimationFrame and an easeOutCubic curve.
 * Equal start/target snaps directly; a change mid-animation cancels the
 * pending frame and continues from the currently displayed value; the frame
 * is also cancelled on destroy.
 *
 * Usage:
 *   <div [countUp]="myNumber"></div>
 */
@Directive({
  selector: '[countUp]',
  standalone: true,
})
export class CountUpDirective {
  readonly countUp = input<number | null>(null);

  private readonly el = inject(ElementRef<HTMLElement>);
  private currentDisplayed = 0;
  private readonly duration = 1200; // ms

  constructor() {
    effect((onCleanup) => {
      const target = this.countUp() ?? 0;
      const start = this.currentDisplayed;
      const range = target - start;

      // Reduced motion: snap straight to the target — no tweening.
      const reducedMotion =
        typeof window !== 'undefined' &&
        window.matchMedia('(prefers-reduced-motion: reduce)').matches;

      if (range === 0 || reducedMotion) {
        this.currentDisplayed = target;
        this.el.nativeElement.textContent = String(target);
        return;
      }

      const startTime = performance.now();
      let rafId = 0;

      const step = (now: number): void => {
        const elapsed = now - startTime;
        const progress = Math.min(elapsed / this.duration, 1);
        // easeOutCubic: fast start, gentle deceleration
        const eased = 1 - Math.pow(1 - progress, 3);
        const current = Math.round(start + range * eased);
        this.currentDisplayed = current;
        this.el.nativeElement.textContent = String(current);

        if (progress < 1) {
          rafId = requestAnimationFrame(step);
        } else {
          // Ensure final value is exactly the target (guards against fp imprecision)
          this.currentDisplayed = target;
          this.el.nativeElement.textContent = String(target);
        }
      };

      rafId = requestAnimationFrame(step);
      onCleanup(() => cancelAnimationFrame(rafId));
    });
  }
}

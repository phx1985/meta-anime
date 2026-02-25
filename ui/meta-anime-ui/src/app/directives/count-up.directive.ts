import { Directive, ElementRef, Input, OnChanges, OnDestroy, SimpleChanges } from '@angular/core';

/**
 * CountUpDirective
 *
 * Animates a numeric counter from its previous displayed value to the new
 * [countUp] input using requestAnimationFrame and an easeOutCubic curve.
 *
 * Usage:
 *   <div [countUp]="myNumber"></div>
 */
@Directive({
  selector: '[countUp]',
  standalone: true,
})
export class CountUpDirective implements OnChanges, OnDestroy {
  @Input() countUp: number | null = null;

  private rafId: number | null = null;
  private currentDisplayed = 0;
  private readonly duration = 1200; // ms

  constructor(private el: ElementRef<HTMLElement>) {}

  ngOnChanges(changes: SimpleChanges): void {
    if ('countUp' in changes) {
      this.animateTo(this.countUp ?? 0);
    }
  }

  private animateTo(target: number): void {
    if (this.rafId !== null) {
      cancelAnimationFrame(this.rafId);
      this.rafId = null;
    }

    const start = this.currentDisplayed;
    const range = target - start;

    if (range === 0) {
      this.el.nativeElement.textContent = String(target);
      return;
    }

    const startTime = performance.now();

    const step = (now: number): void => {
      const elapsed = now - startTime;
      const progress = Math.min(elapsed / this.duration, 1);
      // easeOutCubic: fast start, gentle deceleration
      const eased = 1 - Math.pow(1 - progress, 3);
      const current = Math.round(start + range * eased);
      this.currentDisplayed = current;
      this.el.nativeElement.textContent = String(current);

      if (progress < 1) {
        this.rafId = requestAnimationFrame(step);
      } else {
        // Ensure final value is exactly the target (guards against fp imprecision)
        this.currentDisplayed = target;
        this.el.nativeElement.textContent = String(target);
        this.rafId = null;
      }
    };

    this.rafId = requestAnimationFrame(step);
  }

  ngOnDestroy(): void {
    if (this.rafId !== null) {
      cancelAnimationFrame(this.rafId);
    }
  }
}

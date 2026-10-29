import {
  ChangeDetectionStrategy,
  Component,
  computed,
  Directive,
  effect,
  ElementRef,
  inject,
  input,
  output,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * LaCountUpDirective
 *
 * Animates a numeric metric from its previous displayed value to the new
 * [laCountUp] target (rAF + easeOutCubic) while keeping a fixed decimal count
 * and thousands separators, e.g. `13,475` or `4.8`.
 *
 * Usage:
 *   <span [laCountUp]="totalFileCount()"></span>
 *   <span [laCountUp]="sizeTiB()" [laDecimals]="1"></span>
 */
@Directive({
  selector: '[laCountUp]',
  standalone: true,
})
export class LaCountUpDirective {
  readonly laCountUp = input.required<number>();
  readonly laDecimals = input(0);

  private readonly el = inject(ElementRef<HTMLElement>);
  private displayed = 0;

  constructor() {
    effect((onCleanup) => {
      const target = this.laCountUp();
      const decimals = this.laDecimals();
      const start = this.displayed;
      const range = target - start;

      const format = (value: number): string =>
        value.toLocaleString('en-US', {
          minimumFractionDigits: decimals,
          maximumFractionDigits: decimals,
        });

      if (range === 0) {
        this.el.nativeElement.textContent = format(target);
        return;
      }

      const duration = 1200; // ms
      const startTime = performance.now();
      let rafId = 0;

      const step = (now: number): void => {
        const progress = Math.min((now - startTime) / duration, 1);
        // easeOutCubic: fast start, gentle deceleration
        const eased = 1 - Math.pow(1 - progress, 3);
        const current = progress < 1 ? start + range * eased : target;
        this.displayed = current;
        this.el.nativeElement.textContent = format(current);
        if (progress < 1) {
          rafId = requestAnimationFrame(step);
        }
      };

      rafId = requestAnimationFrame(step);
      onCleanup(() => cancelAnimationFrame(rafId));
    });
  }
}

@Component({
  selector: 'app-la-telemetry-pills',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, LaCountUpDirective],
  templateUrl: './telemetry-pills.component.html',
  styleUrl: './telemetry-pills.component.scss',
})
export class TelemetryPillsComponent {
  readonly totalSizeBytes = input.required<number>();
  readonly totalFileCount = input.required<number>();
  readonly missingSeriesCount = input.required<number>();
  /** Footer context for card 1 — library average bitrate. */
  readonly avgGibPerHour = input(0);
  /** Footer context for card 2 — series count behind the file total. */
  readonly seriesCount = input(0);
  readonly missingFilesClick = output<void>();

  readonly totalSizeDisplay = computed(() => {
    const bytes = this.totalSizeBytes();
    const TiB = 1024 ** 4;
    const GiB = 1024 ** 3;
    if (bytes >= TiB) return { value: +(bytes / TiB).toFixed(1), unit: 'TB' };
    return { value: +(bytes / GiB).toFixed(1), unit: 'GB' };
  });
}

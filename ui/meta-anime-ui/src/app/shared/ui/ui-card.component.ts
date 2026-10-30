import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * UiCardComponent
 *
 * Content-projection wrapper applying the standard card chrome (rounded-xl,
 * border, surface, shadow). Layout, animation and any bespoke polish stay at
 * the call site — host classes pass through, so site-specific rules keep
 * working on the host element.
 */
@Component({
  selector: 'app-ui-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-card.component.html',
  styleUrl: './ui-card.component.scss',
  host: {
    '[style.padding]': 'padding()',
  },
})
export class UiCardComponent {
  /** Card padding — 1.25rem by default; pass any CSS length to override. */
  readonly padding = input<string>('1.25rem');
}

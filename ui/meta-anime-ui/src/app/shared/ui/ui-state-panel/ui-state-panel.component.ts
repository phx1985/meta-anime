import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * UiStatePanelComponent
 *
 * Empty / error state panel for async surfaces: leading icon (content
 * projection), heading, subtext and an optional action CTA. Labels are final
 * strings — translate at the call site.
 */
@Component({
  selector: 'app-ui-state-panel',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-state-panel.component.html',
  styleUrl: './ui-state-panel.component.scss',
})
export class UiStatePanelComponent {
  readonly heading = input.required<string>();
  readonly subtext = input<string>('');
  /** `error` tints the icon well with the danger token. */
  readonly variant = input<'empty' | 'error'>('empty');
  /** CTA label; empty renders no button. */
  readonly actionLabel = input<string>('');
  readonly action = output<void>();
}

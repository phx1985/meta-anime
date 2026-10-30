import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Semantic chip colors — the legacy la-badge--* modifier palette. */
export type UiBadgeColor = 'success' | 'warning' | 'danger' | 'brand' | 'neutral' | 'mixed';

/**
 * UiBadgeComponent
 *
 * Semantic status chip (label via content projection) with the styled
 * [attr.data-tip] hover tooltip — never the native title attribute.
 */
@Component({
  selector: 'app-ui-badge',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-badge.component.html',
  styleUrl: './ui-badge.component.scss',
})
export class UiBadgeComponent {
  readonly color = input<UiBadgeColor>('neutral');

  /** Styled hover tooltip rendered via [attr.data-tip]; empty renders none. */
  readonly tip = input<string>('');

  /** Legacy .la-badge--muted override (slightly stronger neutral tint). */
  readonly muted = input<boolean>(false);
}

import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * UiToggleComponent
 *
 * House iOS-style switch (34×20 track, white knob) shared by settings rows,
 * setup-wizard rows and the decorative switches embedded inside larger
 * buttons (add-series choice cards, transcode batch switch row).
 *
 * Interactive by default: renders a `role="switch"` button that emits
 * `checkedChange` with the negated state. Pass `interactive=false` for the
 * decorative variant (a span) that must live inside an existing button —
 * without an `ariaLabel` it is `aria-hidden`, matching call sites whose outer
 * button already exposes the state.
 *
 * Labels are final strings — translate at the call site.
 */
@Component({
  selector: 'app-ui-toggle',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ui-toggle.component.html',
  styleUrl: './ui-toggle.component.scss',
})
export class UiToggleComponent {
  readonly checked = input<boolean>(false);
  readonly disabled = input<boolean>(false);
  /** Mirrors [attr.aria-label] — translated at the call site. */
  readonly ariaLabel = input<string | null>(null);
  /** Optional visible label rendered next to the switch. */
  readonly label = input<string | null>(null);
  /** Tooltip on the switch element (settings rows). */
  readonly title = input<string | null>(null);
  /** false → decorative visual only, embedded in a larger interactive control. */
  readonly interactive = input<boolean>(true);

  readonly checkedChange = output<boolean>();

  toggle(): void {
    if (this.disabled()) return;
    this.checkedChange.emit(!this.checked());
  }
}

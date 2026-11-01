import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

/** One pill option; `labelKey` is translated inside the component. */
export interface PillOption {
  value: string;
  labelKey: string;
}

/**
 * PillGroupComponent
 *
 * Micro-header + wrapping pill multi-select group — the repeated
 * watch-state / airing / media-type / age-rating / codec / has-file filter
 * block. Purely presentational: the call site owns the selection arrays and
 * their toggle semantics (multi-select toggle, single-select, preset
 * invalidation), so every existing handler binding survives verbatim.
 */
@Component({
  selector: 'app-ui-pill-group',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './pill-group.component.html',
  styleUrl: './pill-group.component.scss',
})
export class PillGroupComponent {
  /** Section label (i18n key, translated here). */
  readonly labelKey = input.required<string>();
  readonly options = input.required<PillOption[]>();
  /** Currently selected values. */
  readonly selected = input<string[]>([]);
  /** Emitted with the clicked option value. */
  readonly optionClick = output<string>();
}

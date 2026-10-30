import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/** One selectable segment; `tip` renders the styled [attr.data-tip] tooltip. */
export interface SegmentedOption {
  id: string;
  label: string;
  tip?: string;
}

/**
 * SegmentedControlComponent
 *
 * Full-width dark segmented control (the `.la-seg` / `.la-seg__btn` idiom from
 * the library-analysis panels) — option-only groups; controls embedding an
 * inline input (the batch BATCH LIMIT group) stay bespoke at their call site.
 */
@Component({
  selector: 'app-segmented-control',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './segmented-control.component.html',
  styleUrl: './segmented-control.component.scss',
})
export class SegmentedControlComponent {
  readonly options = input<SegmentedOption[]>([]);
  readonly value = input<string>('');
  readonly valueChange = output<string>();
  readonly columns = input<2 | 3 | 'auto'>(2);
}

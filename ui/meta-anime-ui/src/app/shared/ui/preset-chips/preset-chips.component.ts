import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { FilterPreset } from '../../../models/anime.models';

/** Payload of `delete` — id plus the originating event (propagation is stopped here;
 *  the delete affordance fires from both click and keyboard activation). */
export interface PresetDeleteEvent {
  id: string;
  event: Event;
}

/**
 * PresetChipsComponent
 *
 * Chip row of filter presets (built-in or saved). Saved chips carry a delete
 * affordance; the active preset is highlighted and (optionally) checked.
 * Shared by the desktop presets bar and the mobile presets sheet — both
 * previously repeated the same apply/delete/active wiring.
 *
 * Label semantics are preserved exactly: built-in chips render their labelKey
 * translated, saved chips render the raw user-entered name.
 */
@Component({
  selector: 'app-ui-preset-chips',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './preset-chips.component.html',
  styleUrl: './preset-chips.component.scss',
})
export class PresetChipsComponent {
  readonly presets = input.required<FilterPreset[]>();
  /** Translated name of the active preset (null when none). */
  readonly activeName = input<string | null>(null);
  /** Render a check mark on the active chip (mobile sheet variant). */
  readonly showCheck = input(false);
  /** Pre-translated delete aria-label. */
  readonly deleteLabel = input('');

  readonly apply = output<FilterPreset>();
  readonly delete = output<PresetDeleteEvent>();

  onDelete(id: string, event: Event): void {
    event.stopPropagation(); // chips are themselves apply buttons
    this.delete.emit({ id, event });
  }
}

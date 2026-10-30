import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * TelemetryBoxComponent
 *
 * Inline stats box: N divided columns, each a micro-label over a metric
 * (top label, bottom value). Chrome (surface, border, column dividers) is
 * owned here; cells are projected so call sites keep their own conditional
 * value markup (loading skeletons, formatted metrics).
 *
 * Project each cell as:
 *   <div class="app-telemetry-box__cell">
 *     <span class="app-telemetry-box__label">…</span>
 *     <span class="app-telemetry-box__value">…</span>   ← add
 *     <span class="app-telemetry-box__value app-telemetry-box__value--success">
 *   </div>
 */
@Component({
  selector: 'app-telemetry-box',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './telemetry-box.component.html',
  styleUrl: './telemetry-box.component.scss',
  host: {
    '[attr.data-cols]': 'columns()',
  },
})
export class TelemetryBoxComponent {
  /** Number of divided columns — 2 by default. */
  readonly columns = input<2 | 3>(2);
}

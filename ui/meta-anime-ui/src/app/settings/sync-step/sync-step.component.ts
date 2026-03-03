import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { SyncStep } from '../../models/sync-progress.models';
import { fmtElapsed } from '../../utils/format.utils';

/** Provider logo paths keyed by step key */
const PROVIDER_IMAGES: Record<string, string> = {
  plex: 'assets/images/plex.svg',
  sonarr: 'assets/images/sonarr.svg',
  mal: 'assets/images/mal.svg',
  kometa: 'assets/images/kometa.png',
  matching: 'assets/images/app.svg',
  app: 'assets/images/app.svg',
};

/**
 * Renders a single sync step row inside the Sync card.
 * Extracted from SettingsComponent for complexity reduction.
 *
 * The host element uses `display: contents` so the inner `<li>` participates
 * directly in the parent `<ol>` layout, preserving valid list semantics.
 */
@Component({
  selector: 'app-sync-step',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  templateUrl: './sync-step.component.html',
  styleUrl: './sync-step.component.scss',
})
export class SyncStepComponent {
  readonly step = input.required<SyncStep>();

  readonly providerImages = PROVIDER_IMAGES;
  readonly fmtElapsed = fmtElapsed;

  get providerImg(): string {
    return this.providerImages[this.step().stepKey] ?? this.providerImages['app'];
  }
}

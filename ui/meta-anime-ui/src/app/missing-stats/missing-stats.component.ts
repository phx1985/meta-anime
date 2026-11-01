import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeStatisticsResponse } from '../models/anime.models';
import { CountUpDirective } from '../directives/count-up.directive';
import { GapDetailModalComponent } from '../gap-detail-modal/gap-detail-modal.component';
import { ProviderStateService } from '../services/provider-state.service';
import { UiCardComponent } from '../shared/ui/ui-card.component';

@Component({
  selector: 'app-missing-stats',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, CountUpDirective, GapDetailModalComponent, UiCardComponent],
  templateUrl: './missing-stats.component.html',
  styleUrl: './missing-stats.component.scss',
})
export class MissingStatsComponent {
  readonly providerState = inject(ProviderStateService);

  stats = input.required<AnimeStatisticsResponse>();

  readonly activeGapType = signal<string | null>(null);
  readonly activeGapLabel = signal<string>('');

  /** Emitted when an item is successfully added to MAL; carries the gapType for the parent to decrement its counter */
  readonly gapCountDecremented = output<string>();

  /** True when at least one gap row is visible (i.e. at least one optional provider is on). */
  readonly hasAnyRow = computed(
    () => this.providerState.plexEnabled() || this.providerState.sonarrEnabled(),
  );

  openGap(gapType: string, labelKey: string): void {
    this.activeGapType.set(gapType);
    this.activeGapLabel.set(labelKey);
  }

  closeGap(): void {
    this.activeGapType.set(null);
  }

  onGapItemResolved(gapType: string): void {
    this.gapCountDecremented.emit(gapType);
  }
}

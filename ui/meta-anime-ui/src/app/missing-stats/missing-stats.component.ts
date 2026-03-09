import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeStatisticsResponse } from '../models/anime.models';
import { CountUpDirective } from '../directives/count-up.directive';
import { GapDetailModalComponent } from '../gap-detail-modal/gap-detail-modal.component';
import { ProviderStateService } from '../services/provider-state.service';

@Component({
  selector: 'app-missing-stats',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, CountUpDirective, GapDetailModalComponent],
  templateUrl: './missing-stats.component.html',
  styleUrl: './missing-stats.component.scss',
})
export class MissingStatsComponent {
  readonly providerState = inject(ProviderStateService);
  private readonly destroyRef = inject(DestroyRef);

  stats = input.required<AnimeStatisticsResponse>();

  readonly activeGapType = signal<string | null>(null);
  readonly activeGapLabel = signal<string>('');

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
}

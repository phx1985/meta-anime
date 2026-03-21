import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { CountUpDirective } from '../../directives/count-up.directive';

@Component({
  selector: 'app-la-telemetry-pills',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, CountUpDirective],
  templateUrl: './telemetry-pills.component.html',
  styleUrl: './telemetry-pills.component.scss',
})
export class TelemetryPillsComponent {
  readonly totalSizeBytes = input.required<number>();
  readonly totalFileCount = input.required<number>();
  readonly missingSeriesCount = input.required<number>();

  readonly totalSizeDisplay = computed(() => {
    const bytes = this.totalSizeBytes();
    const TiB = 1024 ** 4;
    const GiB = 1024 ** 3;
    if (bytes >= TiB) return { value: +(bytes / TiB).toFixed(1), unit: 'TB' };
    return { value: +(bytes / GiB).toFixed(1), unit: 'GB' };
  });
}

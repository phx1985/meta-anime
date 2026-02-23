import { Component, Input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeStatisticsResponse } from '../models/anime.models';
import { CountUpDirective } from '../directives/count-up.directive';

@Component({
  selector: 'app-missing-stats',
  standalone: true,
  imports: [TranslatePipe, CountUpDirective],
  templateUrl: './missing-stats.component.html',
  styleUrl: './missing-stats.component.scss'
})
export class MissingStatsComponent {
  @Input() stats!: AnimeStatisticsResponse;
}

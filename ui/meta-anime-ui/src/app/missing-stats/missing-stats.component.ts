import { Component, Input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AnimeStatisticsResponse } from '../models/anime.models';

@Component({
  selector: 'app-missing-stats',
  standalone: true,
  imports: [TranslatePipe],
  templateUrl: './missing-stats.component.html',
  styleUrl: './missing-stats.component.scss'
})
export class MissingStatsComponent {
  @Input() stats!: AnimeStatisticsResponse;
}

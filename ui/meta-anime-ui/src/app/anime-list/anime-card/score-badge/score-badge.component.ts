import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';

/** Circumference of the SVG arc (2π × r, r=15) */
const ARC_CIRCUMFERENCE = 94.25;

/**
 * Reusable circular arc score badge.
 * Used in AnimeCardComponent for the MAL community score and the user's personal score.
 */
@Component({
  selector: 'app-score-badge',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './score-badge.component.html',
  styleUrl: './score-badge.component.scss',
})
export class ScoreBadgeComponent {
  /** Numeric score (0–10) */
  @Input({ required: true }) score!: number;
  /** Short label rendered below the number (e.g. "MAL", or a pre-translated string) */
  @Input() label = 'MAL';
  /** Whether to render the "my score" green arc variant */
  @Input() mine = false;
  /** i18n key used as tooltip title (translated internally) */
  @Input() titleKey = 'animeCard.malScore';

  readonly arcCircumference = ARC_CIRCUMFERENCE;

  /** Convert a 0–10 score to a 0–100 arc fill percentage */
  scorePct(score: number): number {
    return Math.round(Math.min(10, Math.max(0, score)) * 10);
  }
}

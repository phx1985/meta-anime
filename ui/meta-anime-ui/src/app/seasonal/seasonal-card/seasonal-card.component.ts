import { Component, Input, signal, inject, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { trigger, state, style, animate, transition } from '@angular/animations';
import { SeasonalAnimeItem } from '../../models/anime.models';
import { ScoreBadgeComponent } from '../../anime-list/anime-card/score-badge/score-badge.component';

const TRUNCATE_AT = 180;

@Component({
  selector: 'app-seasonal-card',
  standalone: true,
  imports: [CommonModule, TranslatePipe, ScoreBadgeComponent],
  templateUrl: './seasonal-card.component.html',
  styleUrl: './seasonal-card.component.scss',
  animations: [
    trigger('expandCollapse', [
      state('collapsed', style({ height: '3.75rem', overflow: 'hidden' })),
      state('expanded',  style({ height: '*',       overflow: 'hidden' })),
      transition('collapsed => expanded', animate('380ms cubic-bezier(0.4, 0, 0.2, 1)')),
      transition('expanded => collapsed', animate('280ms cubic-bezier(0.4, 0, 1, 1)')),
    ])
  ]
})
export class SeasonalCardComponent {
  @Input({ required: true }) anime!: SeasonalAnimeItem;

  private translate = inject(TranslateService);

  descExpanded = signal(false);
  showLightbox = signal(false);

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.showLightbox()) this.showLightbox.set(false);
  }

  get coverImageUrl(): string | undefined {
    return this.anime.coverImageUrl;
  }

  get displayTitle(): string {
    return this.anime.title;
  }

  get description(): string {
    return this.anime.synopsis ?? '';
  }

  get truncatedDescription(): string {
    const d = this.description;
    if (!d) return '';
    return this.descExpanded() || d.length <= TRUNCATE_AT ? d : d.slice(0, TRUNCATE_AT) + '…';
  }

  get genres(): string[] {
    return this.anime.genres ?? [];
  }

  translateGenre(name: string): string {
    const key = `genres.${name}`;
    const t = this.translate.instant(key);
    return t !== key ? t : name;
  }

  get malScore(): number {
    return this.anime.malScore ?? 0;
  }

  get startDate(): string | undefined {
    return this.anime.startDate;
  }

  get mediaType(): string | undefined {
    return this.anime.mediaType;
  }

  mediaTypeLabel(type: string): string {
    const key = `animeCard.mediaType.${type}`;
    const t = this.translate.instant(key);
    return t !== key ? t : type.toUpperCase();
  }

  get seriesStatus(): string | undefined {
    return this.anime.status;
  }

  seriesStatusLabel(status: string): string {
    const key = `animeCard.seriesStatus.${status}`;
    const t = this.translate.instant(key);
    return t !== key ? t : status.replace(/_/g, ' ');
  }

  get malUrl(): string | null {
    return this.anime.malUrl ?? null;
  }

  toggleDesc(): void {
    this.descExpanded.update((v) => !v);
  }

  openLightbox(): void {
    if (this.coverImageUrl) this.showLightbox.set(true);
  }

  closeLightbox(): void {
    this.showLightbox.set(false);
  }
}

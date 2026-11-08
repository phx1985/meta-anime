import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type ProviderIconType = 'mal' | 'sonarr' | 'plex' | 'kometa' | 'matching';

/**
 * Canonical brand icon for every provider. Reuses the real assets that already ship in
 * `assets/images` — no more generic play triangles standing in for Plex.
 */
@Component({
  selector: 'app-provider-icon',
  standalone: true,
  template: `
    @switch (provider()) {
      @case ('matching') {
        <svg [attr.width]="size()" [attr.height]="size()" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
          <path d="M8 3H5a2 2 0 0 0-2 2v3" /><path d="M16 3h3a2 2 0 0 1 2 2v3" />
          <path d="M8 21H5a2 2 0 0 1-2-2v-3" /><path d="M16 21h3a2 2 0 0 0 2-2v-3" />
          <path d="m9 12 2 2 4-4" />
        </svg>
      }
      @default {
        <img [src]="asset()" [attr.width]="size()" [attr.height]="size()" [alt]="provider()"
          class="provider-icon__img" loading="lazy" />
      }
    }
  `,
  styles: [`
    :host { display: inline-flex; align-items: center; justify-content: center; }
    .provider-icon__img { display: block; object-fit: contain; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProviderIconComponent {
  readonly provider = input<ProviderIconType>('mal');
  readonly size = input<number>(18);

  readonly asset = computed(() => {
    switch (this.provider()) {
      case 'sonarr': return 'assets/images/sonarr.svg';
      case 'plex': return 'assets/images/plex.svg';
      case 'kometa': return 'assets/images/kometa.png';
      default: return 'assets/images/mal.svg';
    }
  });
}

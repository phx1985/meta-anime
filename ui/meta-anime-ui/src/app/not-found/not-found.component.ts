import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

export interface NotFoundCharacter {
  nameKey: string;
  quoteKey: string;
  imagePath: string;
}

export const NOT_FOUND_CHARACTERS: readonly NotFoundCharacter[] = [
  {
    nameKey: 'notFound.characters.goku.name',
    quoteKey: 'notFound.characters.goku.quote',
    imagePath: 'assets/images/404/goku.webp',
  },
  {
    nameKey: 'notFound.characters.luffy.name',
    quoteKey: 'notFound.characters.luffy.quote',
    imagePath: 'assets/images/404/luffy.webp',
  },
  {
    nameKey: 'notFound.characters.naruto.name',
    quoteKey: 'notFound.characters.naruto.quote',
    imagePath: 'assets/images/404/naruto.webp',
  },
  {
    nameKey: 'notFound.characters.sailorMoon.name',
    quoteKey: 'notFound.characters.sailorMoon.quote',
    imagePath: 'assets/images/404/sailor-moon.webp',
  },
  {
    nameKey: 'notFound.characters.edwardElric.name',
    quoteKey: 'notFound.characters.edwardElric.quote',
    imagePath: 'assets/images/404/edward-elric.webp',
  },
  {
    nameKey: 'notFound.characters.lelouch.name',
    quoteKey: 'notFound.characters.lelouch.quote',
    imagePath: 'assets/images/404/lelouch.webp',
  },
  {
    nameKey: 'notFound.characters.mikasa.name',
    quoteKey: 'notFound.characters.mikasa.quote',
    imagePath: 'assets/images/404/mikasa.webp',
  },
  {
    nameKey: 'notFound.characters.tanjiro.name',
    quoteKey: 'notFound.characters.tanjiro.quote',
    imagePath: 'assets/images/404/tanjiro.webp',
  },
  {
    nameKey: 'notFound.characters.spike.name',
    quoteKey: 'notFound.characters.spike.quote',
    imagePath: 'assets/images/404/spike.webp',
  },
  {
    nameKey: 'notFound.characters.light.name',
    quoteKey: 'notFound.characters.light.quote',
    imagePath: 'assets/images/404/light.webp',
  },
] as const;

@Component({
  selector: 'app-not-found',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslatePipe],
  templateUrl: './not-found.component.html',
  styleUrl: './not-found.component.scss',
})
export class NotFoundComponent {
  /** Randomly selected character — determined once at construction, never via RxJS. */
  readonly character = signal<NotFoundCharacter>(
    NOT_FOUND_CHARACTERS[Math.floor(Math.random() * NOT_FOUND_CHARACTERS.length)],
  );
}

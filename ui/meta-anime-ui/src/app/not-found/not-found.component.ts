import { ChangeDetectionStrategy, Component, signal, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../i18n/translate.pipe';

export interface AnimeCharacter {
  readonly nameKey: string;
  readonly quoteKey: string;
  readonly imagePath: string;
}

const ANIME_CHARACTERS: ReadonlyArray<AnimeCharacter> = [
  {
    nameKey: 'NOT_FOUND.CHARACTERS.GOKU.NAME',
    quoteKey: 'NOT_FOUND.CHARACTERS.GOKU.QUOTE',
    imagePath: 'assets/images/404/goku.webp',
  },
  {
    nameKey: 'NOT_FOUND.CHARACTERS.LUFFY.NAME',
    quoteKey: 'NOT_FOUND.CHARACTERS.LUFFY.QUOTE',
    imagePath: 'assets/images/404/luffy.webp',
  },
  {
    nameKey: 'NOT_FOUND.CHARACTERS.SAILOR_MOON.NAME',
    quoteKey: 'NOT_FOUND.CHARACTERS.SAILOR_MOON.QUOTE',
    imagePath: 'assets/images/404/sailor-moon.webp',
  },
  {
    nameKey: 'NOT_FOUND.CHARACTERS.NARUTO.NAME',
    quoteKey: 'NOT_FOUND.CHARACTERS.NARUTO.QUOTE',
    imagePath: 'assets/images/404/naruto.webp',
  },
  {
    nameKey: 'NOT_FOUND.CHARACTERS.EDWARD.NAME',
    quoteKey: 'NOT_FOUND.CHARACTERS.EDWARD.QUOTE',
    imagePath: 'assets/images/404/edward.webp',
  },
  {
    nameKey: 'NOT_FOUND.CHARACTERS.LIGHT.NAME',
    quoteKey: 'NOT_FOUND.CHARACTERS.LIGHT.QUOTE',
    imagePath: 'assets/images/404/light.webp',
  },
  {
    nameKey: 'NOT_FOUND.CHARACTERS.SPIKE.NAME',
    quoteKey: 'NOT_FOUND.CHARACTERS.SPIKE.QUOTE',
    imagePath: 'assets/images/404/spike.webp',
  },
  {
    nameKey: 'NOT_FOUND.CHARACTERS.LEVI.NAME',
    quoteKey: 'NOT_FOUND.CHARACTERS.LEVI.QUOTE',
    imagePath: 'assets/images/404/levi.webp',
  },
  {
    nameKey: 'NOT_FOUND.CHARACTERS.ICHIGO.NAME',
    quoteKey: 'NOT_FOUND.CHARACTERS.ICHIGO.QUOTE',
    imagePath: 'assets/images/404/ichigo.webp',
  },
  {
    nameKey: 'NOT_FOUND.CHARACTERS.VEGETA.NAME',
    quoteKey: 'NOT_FOUND.CHARACTERS.VEGETA.QUOTE',
    imagePath: 'assets/images/404/vegeta.webp',
  },
] as const;

@Component({
  selector: 'app-not-found',
  standalone: true,
  imports: [RouterLink, TranslatePipe],
  templateUrl: './not-found.component.html',
  styleUrl: './not-found.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFoundComponent {
  private readonly randomIndex = signal(
    Math.floor(Math.random() * ANIME_CHARACTERS.length),
  );

  readonly character = computed(() => ANIME_CHARACTERS[this.randomIndex()]);
}

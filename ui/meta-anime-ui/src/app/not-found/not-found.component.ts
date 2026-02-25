import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';

export interface NotFoundCharacter {
  nameKey: string;
  quoteKey: string;
}

export const NOT_FOUND_CHARACTERS: readonly NotFoundCharacter[] = [
  { nameKey: 'notFound.characters.goku.name', quoteKey: 'notFound.characters.goku.quote' },
  { nameKey: 'notFound.characters.luffy.name', quoteKey: 'notFound.characters.luffy.quote' },
  { nameKey: 'notFound.characters.naruto.name', quoteKey: 'notFound.characters.naruto.quote' },
  { nameKey: 'notFound.characters.sailorMoon.name', quoteKey: 'notFound.characters.sailorMoon.quote' },
  { nameKey: 'notFound.characters.edwardElric.name', quoteKey: 'notFound.characters.edwardElric.quote' },
  { nameKey: 'notFound.characters.lelouch.name', quoteKey: 'notFound.characters.lelouch.quote' },
  { nameKey: 'notFound.characters.mikasa.name', quoteKey: 'notFound.characters.mikasa.quote' },
  { nameKey: 'notFound.characters.tanjiro.name', quoteKey: 'notFound.characters.tanjiro.quote' },
  { nameKey: 'notFound.characters.spike.name', quoteKey: 'notFound.characters.spike.quote' },
  { nameKey: 'notFound.characters.light.name', quoteKey: 'notFound.characters.light.quote' },
] as const;

/** Subtitle keys randomly rotated on each page load. */
export const NOT_FOUND_SUBTITLES: readonly string[] = [
  'notFound.subtitle.dimension',
  'notFound.subtitle.trainingArc',
  'notFound.subtitle.plotTwist',
  'notFound.subtitle.houdini',
  'notFound.subtitle.void',
  'notFound.subtitle.kamehameha',
  'notFound.subtitle.timeline',
  'notFound.subtitle.questGone',
  'notFound.subtitle.uncharted',
  'notFound.subtitle.endOfInternet',
] as const;

function pickRandom<T>(arr: readonly T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

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
  readonly character = signal<NotFoundCharacter>(pickRandom(NOT_FOUND_CHARACTERS));

  /** Randomly selected subtitle key — independent of character choice. */
  readonly subtitleKey = signal<string>(pickRandom(NOT_FOUND_SUBTITLES));
}

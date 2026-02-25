import { Injectable, signal, computed } from '@angular/core';
import { EN_TRANSLATIONS, FR_TRANSLATIONS, Translations } from './translations';

export type Language = 'en' | 'fr';

@Injectable({ providedIn: 'root' })
export class TranslationService {
  readonly currentLang = signal<Language>('en');

  private readonly translationMap = computed<Translations>(() =>
    this.currentLang() === 'fr' ? FR_TRANSLATIONS : EN_TRANSLATIONS,
  );

  translate(key: string): string {
    return this.translationMap()[key] ?? key;
  }

  setLanguage(lang: Language): void {
    this.currentLang.set(lang);
  }
}

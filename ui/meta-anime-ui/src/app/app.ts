import { Component, inject, signal } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ThemeService } from './services/theme.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected themeService = inject(ThemeService);
  protected translate = inject(TranslateService);
  protected currentLang = signal<string>('en');

  constructor() {
    this.translate.use('en');
  }

  toggleTheme(): void {
    this.themeService.toggleTheme();
  }

  switchLanguage(): void {
    const next = this.currentLang() === 'en' ? 'fr' : 'en';
    this.currentLang.set(next);
    this.translate.use(next);
  }
}

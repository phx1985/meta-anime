import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ThemeService } from './services/theme.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected themeService = inject(ThemeService);
  protected translate = inject(TranslateService);

  toggleTheme(): void {
    this.themeService.toggleTheme();
  }

  switchLanguage(lang: string): void {
    this.translate.use(lang);
  }
}

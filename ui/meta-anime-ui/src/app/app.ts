import { Component, inject, signal, OnInit } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ThemeService } from './services/theme.service';
import { SettingsService } from './services/settings.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  protected themeService     = inject(ThemeService);
  protected translate        = inject(TranslateService);
  private   settingsService  = inject(SettingsService);
  private   router           = inject(Router);
  protected currentLang      = signal<string>('en');
  protected menuOpen         = signal<boolean>(false);
  protected showNoSyncToast  = signal<boolean>(false);

  constructor() {
    this.translate.use('en');
  }

  ngOnInit(): void {
    this.settingsService.getSyncStatus().subscribe({
      next: status => {
        if (!status.lastSyncDate) {
          this.showNoSyncToast.set(true);
        }
      },
      error: () => { /* non-critical */ }
    });
  }

  dismissNoSyncToast(): void {
    this.showNoSyncToast.set(false);
  }

  goToSync(): void {
    this.showNoSyncToast.set(false);
    this.router.navigate(['/settings'], { fragment: 'sync-section' });
  }

  toggleTheme(): void {
    this.themeService.toggleTheme();
  }

  toggleFollowOs(): void {
    this.themeService.setFollowOsTheme(!this.themeService.followOsTheme());
  }

  toggleMenu(): void {
    this.menuOpen.update(v => !v);
  }

  closeMenu(): void {
    this.menuOpen.set(false);
  }

  switchLanguage(): void {
    const next = this.currentLang() === 'en' ? 'fr' : 'en';
    this.currentLang.set(next);
    this.translate.use(next);
  }
}

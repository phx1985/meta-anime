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

  /** Provider home-page URLs (populated from settings) */
  protected plexHomeUrl   = signal<string | null>(null);
  protected sonarrHomeUrl = signal<string | null>(null);
  protected readonly malHomeUrl = 'https://myanimelist.net';

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

    // Load provider URLs for navbar quick-links
    this.settingsService.getAll().subscribe({
      next: settings => {
        const plexHost     = settings.find(s => s.key === 'Plex:Host')?.value ?? null;
        const sonarrRemote = settings.find(s => s.key === 'Sonarr:RemoteUrl')?.value ?? null;
        const sonarrHost   = settings.find(s => s.key === 'Sonarr:Host')?.value ?? null;
        const sonarrUrl    = sonarrRemote || sonarrHost;
        // Always link to the Plex web app (not the local instance)
        if (plexHost)   this.plexHomeUrl.set('https://app.plex.tv');
        if (sonarrUrl)  this.sonarrHomeUrl.set(sonarrUrl);
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

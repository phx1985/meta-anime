import { Component, inject, signal, OnInit, HostListener } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ThemeService } from './services/theme.service';
import { SettingsService } from './services/settings.service';
import { NotificationService, AppNotification } from './services/notification.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  protected themeService    = inject(ThemeService);
  protected translate       = inject(TranslateService);
  protected notifService    = inject(NotificationService);
  private   settingsService = inject(SettingsService);
  private   router          = inject(Router);

  protected currentLang     = signal<string>('en');
  protected menuOpen        = signal<boolean>(false);
  protected notifCenterOpen = signal<boolean>(false);

  /** Provider home-page URLs (populated from settings) */
  protected plexHomeUrl   = signal<string | null>(null);
  protected sonarrHomeUrl = signal<string | null>(null);
  protected readonly malHomeUrl = 'https://myanimelist.net';

  constructor() {
    this.translate.use('en');
  }

  ngOnInit(): void {
    // Push "Welcome" notification once ever (idempotent by fixed id)
    this.notifService.push({
      id: 'welcome-v1',
      severity: 'info',
      titleKey: 'notifications.welcome.title',
      messageKey: 'notifications.welcome.message',
      action: { labelKey: 'notifications.welcome.action', route: ['/settings'], fragment: 'sync-section' },
    });

    // Push "no sync yet" warning if library has never been synced
    this.settingsService.getSyncStatus().subscribe({
      next: status => {
        if (!status.lastSyncDate) {
          this.notifService.push({
            id: 'no-sync-needed',
            severity: 'warning',
            titleKey: 'notifications.noSync.title',
            messageKey: 'notifications.noSync.message',
            action: { labelKey: 'notifications.noSync.action', route: ['/settings'], fragment: 'sync-section' },
          });
        }
      },
      error: () => { /* non-critical */ }
    });

    // Load provider URLs for navbar quick-links
    this.settingsService.getAll().subscribe({
      next: settings => {
        const plexHost  = settings.find(s => s.key === 'Plex:Host')?.value ?? null;
        const sonarrUrl = settings.find(s => s.key === 'Sonarr:RemoteUrl')?.value
                       || settings.find(s => s.key === 'Sonarr:Host')?.value
                       || null;
        if (plexHost)  this.plexHomeUrl.set('https://app.plex.tv');
        if (sonarrUrl) this.sonarrHomeUrl.set(sonarrUrl);
      },
      error: () => { /* non-critical */ }
    });
  }

  /** Close notification center when clicking anywhere outside it */
  @HostListener('document:click')
  onDocumentClick(): void {
    this.notifCenterOpen.set(false);
  }

  toggleNotifCenter(event: Event): void {
    event.stopPropagation();
    this.notifCenterOpen.update(v => !v);
  }

  handleNotifClick(notif: AppNotification): void {
    this.notifService.acknowledge(notif.id);
    this.notifCenterOpen.set(false);
    if (notif.action) {
      this.router.navigate(notif.action.route,
        notif.action.fragment ? { fragment: notif.action.fragment } : undefined
      );
    }
  }

  handleToastAction(toast: AppNotification): void {
    this.notifService.acknowledge(toast.id);
    this.notifService.dismissToast(toast.id);
    if (toast.action) {
      this.router.navigate(toast.action.route,
        toast.action.fragment ? { fragment: toast.action.fragment } : undefined
      );
    }
  }

  /** Human-readable relative timestamp — language-agnostic short form */
  formatRelTime(ts: number): string {
    const diff = Date.now() - ts;
    if (diff < 60_000)     return this.translate.instant('notifications.time.justNow');
    if (diff < 3_600_000)  return this.translate.instant('notifications.time.minutesAgo',
                                    { n: Math.floor(diff / 60_000) });
    if (diff < 86_400_000) return this.translate.instant('notifications.time.hoursAgo',
                                    { n: Math.floor(diff / 3_600_000) });
    return this.translate.instant('notifications.time.daysAgo',
             { n: Math.floor(diff / 86_400_000) });
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

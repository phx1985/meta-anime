import {
  Component,
  inject,
  signal,
  OnInit,
  HostListener,
  DestroyRef,
  ViewChild,
  ElementRef,
  AfterViewInit,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { filter } from 'rxjs/operators';
import { ThemeService } from './services/theme.service';
import { SettingsService } from './services/settings.service';
import { NotificationService, AppNotification } from './services/notification.service';
import { SyncSignalRService } from './services/sync-signalr.service';
import { ProviderStateService } from './services/provider-state.service';
import { LuckyPickComponent } from './lucky-pick/lucky-pick.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, LuckyPickComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit, AfterViewInit {
  protected themeService = inject(ThemeService);
  protected translate = inject(TranslateService);
  protected notifService = inject(NotificationService);
  private settingsService = inject(SettingsService);
  private syncSignalR = inject(SyncSignalRService);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);
  readonly providerState = inject(ProviderStateService);

  @ViewChild('navbarEl', { read: ElementRef }) private navbarEl?: ElementRef<HTMLElement>;

  protected currentLang = signal<string>('en');
  protected menuOpen = signal<boolean>(false);
  protected notifCenterOpen = signal<boolean>(false);
  /** Dynamic top offset for the toast stack — tracks the actual navbar height */
  protected toastTopPx = signal<number>(72); // ~4.5rem default

  /** Controls the Lucky Pick overlay visibility */
  protected luckyPickOpen = signal<boolean>(false);
  /** Prevent rapid-fire clicks from re-opening while already open */
  private luckyPickBusy = false;

  /** Provider home-page URLs (populated from settings) */
  protected plexHomeUrl = signal<string | null>(null);
  protected sonarrHomeUrl = signal<string | null>(null);
  protected readonly malHomeUrl = 'https://myanimelist.net';

  /** Controls the disabled-provider interception modal */
  protected disabledProviderModal = signal<'plex' | 'sonarr' | null>(null);

  constructor() {
    this.translate.use('en');
  }

  ngOnInit(): void {
    // Connect to SignalR hub globally so sync done/error notifications work on any page
    this.syncSignalR.connect();

    // Sync done → success notification (fires on every transition false→true, not on replay)
    this.syncSignalR.syncDone$
      .pipe(
        filter((v) => v),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        this.notifService.push({
          id: `sync-done-${Date.now()}`,
          severity: 'success',
          titleKey: 'notifications.sync.doneTitle',
          messageKey: 'notifications.sync.doneMessage',
        });
      });

    // Sync error → error notification
    this.syncSignalR.syncError$
      .pipe(
        filter((v) => v),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        this.notifService.push({
          id: `sync-error-${Date.now()}`,
          severity: 'error',
          titleKey: 'notifications.sync.errorTitle',
          messageKey: 'notifications.sync.errorMessage',
          action: {
            labelKey: 'notifications.sync.viewLog',
            route: ['/settings'],
            fragment: 'sync-section',
          },
        });
      });

    // Push "Welcome" notification once ever (idempotent by fixed id)
    this.notifService.push({
      id: 'welcome-v1',
      severity: 'info',
      titleKey: 'notifications.welcome.title',
      messageKey: 'notifications.welcome.message',
      action: {
        labelKey: 'notifications.welcome.action',
        route: ['/settings'],
        fragment: 'sync-section',
      },
    });

    // Push "no sync yet" warning if library has never been synced
    this.settingsService.getSyncStatus().subscribe({
      next: (status) => {
        if (!status.lastSyncDate) {
          this.notifService.push({
            id: 'no-sync-needed',
            severity: 'warning',
            titleKey: 'notifications.noSync.title',
            messageKey: 'notifications.noSync.message',
            action: {
              labelKey: 'notifications.noSync.action',
              route: ['/settings'],
              fragment: 'sync-section',
            },
          });
        }
      },
      error: () => {
        /* non-critical */
      },
    });

    // Load provider URLs for navbar quick-links
    this.settingsService.getAll().subscribe({
      next: (settings) => {
        const plexHost = settings.find((s) => s.key === 'Plex:Host')?.value ?? null;
        const sonarrUrl =
          settings.find((s) => s.key === 'Sonarr:RemoteUrl')?.value ||
          settings.find((s) => s.key === 'Sonarr:Host')?.value ||
          null;
        if (plexHost) this.plexHomeUrl.set('https://app.plex.tv');
        if (sonarrUrl) this.sonarrHomeUrl.set(sonarrUrl);
      },
      error: () => {
        /* non-critical */
      },
    });
  }

  /** Close notification center when clicking anywhere outside it */
  @HostListener('document:click')
  onDocumentClick(): void {
    this.notifCenterOpen.set(false);
  }

  toggleNotifCenter(event: Event): void {
    event.stopPropagation();
    this.notifCenterOpen.update((v) => !v);
  }

  openLuckyPick(): void {
    if (this.luckyPickBusy || this.luckyPickOpen()) return;
    this.luckyPickBusy = true;
    this.luckyPickOpen.set(true);
    // Reset busy flag after a short guard window
    setTimeout(() => (this.luckyPickBusy = false), 500);
  }

  closeLuckyPick(): void {
    this.luckyPickOpen.set(false);
  }

  /** Called when a disabled provider link is clicked — shows the interception modal */
  openDisabledProviderModal(provider: 'plex' | 'sonarr', event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.disabledProviderModal.set(provider);
  }

  dismissDisabledProviderModal(): void {
    this.disabledProviderModal.set(null);
  }

  goToProviderSettings(provider: 'plex' | 'sonarr'): void {
    this.disabledProviderModal.set(null);
    this.router.navigate(['/settings'], { fragment: `section-${provider}` });
  }

  /** Convenience method for template — dismisses modal and navigates to provider settings. */
  dismissAndGoToSettings(): void {
    const provider = this.disabledProviderModal();
    if (provider) {
      this.goToProviderSettings(provider);
    }
  }

  handleNotifClick(notif: AppNotification): void {
    this.notifService.acknowledge(notif.id);
    this.notifCenterOpen.set(false);
    if (notif.action) {
      this.router.navigate(
        notif.action.route,
        notif.action.fragment ? { fragment: notif.action.fragment } : undefined,
      );
    }
  }

  handleToastAction(toast: AppNotification): void {
    this.notifService.acknowledge(toast.id);
    this.notifService.dismissToast(toast.id);
    if (toast.action) {
      this.router.navigate(
        toast.action.route,
        toast.action.fragment ? { fragment: toast.action.fragment } : undefined,
      );
    }
  }

  /** Human-readable relative timestamp — language-agnostic short form */
  formatRelTime(ts: number): string {
    const diff = Date.now() - ts;
    if (diff < 60_000) return this.translate.instant('notifications.time.justNow');
    if (diff < 3_600_000)
      return this.translate.instant('notifications.time.minutesAgo', {
        n: Math.floor(diff / 60_000),
      });
    if (diff < 86_400_000)
      return this.translate.instant('notifications.time.hoursAgo', {
        n: Math.floor(diff / 3_600_000),
      });
    return this.translate.instant('notifications.time.daysAgo', {
      n: Math.floor(diff / 86_400_000),
    });
  }

  /** Duration of the CSS `.navbar-collapse` max-height transition — keep in sync with app.scss */
  private static readonly NAVBAR_COLLAPSE_MS = 310;

  ngAfterViewInit(): void {
    this.syncToastTop();
  }

  /** Measure navbar height and update toast-stack top offset */
  private syncToastTop(): void {
    const h = this.navbarEl?.nativeElement?.offsetHeight;
    if (h) this.toastTopPx.set(h + 8);
  }

  toggleTheme(): void {
    this.themeService.toggleTheme();
  }

  toggleFollowOs(): void {
    this.themeService.setFollowOsTheme(!this.themeService.followOsTheme());
  }

  toggleMenu(): void {
    this.menuOpen.update((v) => !v);
    // Measure after the CSS max-height transition completes
    setTimeout(() => this.syncToastTop(), App.NAVBAR_COLLAPSE_MS);
  }

  closeMenu(): void {
    this.menuOpen.set(false);
    setTimeout(() => this.syncToastTop(), App.NAVBAR_COLLAPSE_MS);
  }

  switchLanguage(): void {
    const next = this.currentLang() === 'en' ? 'fr' : 'en';
    this.currentLang.set(next);
    this.translate.use(next);
  }

  /** Called when the user clicks × on a notification-center item. */
  onNotifDismiss(id: string, event: MouseEvent): void {
    event.stopPropagation();
    this.notifService.dismissNotifItem(id);
  }
}

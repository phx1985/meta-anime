import {
  ChangeDetectionStrategy,
  Component,
  computed,
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
import { RouterOutlet, RouterLink, RouterLinkActive, Router, NavigationEnd } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { UpperCasePipe } from '@angular/common';
import { filter } from 'rxjs/operators';
import { ThemeService } from './services/theme.service';
import { SettingsService } from './services/settings.service';
import { NotificationService, AppNotification } from './services/notification.service';
import { SyncSignalRService } from './services/sync-signalr.service';
import { ProviderStateService } from './services/provider-state.service';
import { PopupService } from './services/popup.service';
import { PlexDeepLinkService } from './services/plex-deep-link.service';
import { LuckyPickComponent } from './lucky-pick/lucky-pick.component';
import { FranchiseConstellationComponent } from './franchise-constellation/franchise-constellation.component';
import { FranchiseConstellationService } from './services/franchise-constellation.service';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, LuckyPickComponent, UpperCasePipe, FranchiseConstellationComponent],
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
  readonly popupService = inject(PopupService);
  readonly franchiseConstellationSvc = inject(FranchiseConstellationService);
  private plexDeepLink = inject(PlexDeepLinkService);

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

  /** Available languages for the dropdown */
  protected readonly availableLanguages: ReadonlyArray<{ code: string; label: string }> = [
    { code: 'en', label: 'English' },
    { code: 'fr', label: 'Français' },
  ];
  /** True while the language dropdown is open */
  protected langDropdownOpen = signal<boolean>(false);

  /** Provider home-page URLs (populated from settings) */
  protected plexHomeUrl = signal<string | null>(null);
  protected sonarrHomeUrl = signal<string | null>(null);
  protected readonly malHomeUrl = 'https://myanimelist.net';

  /** True while the bottom nav bar is retracted (scroll-down hide on mobile) */
  protected bottomNavHidden = signal<boolean>(false);
  /**
   * True whenever any overlay / bottom-sheet / modal is open.
   * Combines signals from this component (Lucky Pick, side drawer) and from
   * PopupService (disabled-provider, synopsis, sync-prompt modals, and the
   * child-component filter / presets sheets registered via `childOverlayOpen`).
   */
  protected anyOverlayOpen = computed(
    () =>
      this.luckyPickOpen() ||
      this.menuOpen() ||
      this.popupService.childOverlayOpen() ||
      this.popupService.addProviderModalOpen() ||
      this.popupService.franchiseConstellationOpen() ||
      !!this.popupService.disabledProvider() ||
      !!this.popupService.synopsisTitle() ||
      !!this.popupService.syncPromptProvider(),
  );
  /** i18n key of the active route – displayed as a page label in the top header on mobile */
  protected currentPageKey = signal<string>('');
  /** Tracks the last scroll position for scroll-direction detection */
  private lastScrollY = 0;
  /** Minimum scroll delta (px) before direction is detected, avoids micro-jitter */
  private static readonly SCROLL_DELTA_THRESHOLD = 4;
  /** If scroll position is at or below this many px from the top, always reveal the nav */
  private static readonly SCROLL_TOP_THRESHOLD = 40;

  /** Route → nav i18n key mapping */
  private static readonly PAGE_KEYS: Record<string, string> = {
    '/animes': 'nav.animes',
    '/seasonal': 'nav.seasonal',
    '/settings': 'nav.settings',
  };

  /** Controls the disabled-provider interception modal — delegated to PopupService */
  protected get disabledProviderModal() { return this.popupService.disabledProvider; }
  protected get modalLeaving() { return this.popupService.disabledProviderLeaving; }

  /** Controls the synopsis modal — delegated to PopupService */
  protected get synopsisModal() { return this.popupService.synopsisTitle; }
  protected get synopsisBody() { return this.popupService.synopsisBody; }
  protected get synopsisLeaving() { return this.popupService.synopsisLeaving; }

  constructor() {
    this.translate.use('en');
  }

  ngOnInit(): void {
    // Connect to SignalR hub globally so sync done/error notifications work on any page
    this.syncSignalR.connect();

    // Track active route for the mobile page-context label
    this.router.events
      .pipe(
        filter((e): e is NavigationEnd => e instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((e) => {
        // Strip query params and hash, then map to a page i18n key ('' for unmapped routes)
        const path = '/' + e.urlAfterRedirects.replace(/^\//, '').split('?')[0].split('#')[0].split('/')[0];
        this.currentPageKey.set(App.PAGE_KEYS[path] ?? '');
        // Always reveal the bottom nav when navigating to a new page
        this.bottomNavHidden.set(false);
        this.lastScrollY = 0;
      });

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

  /** Scroll-to-hide/show the bottom navigation bar on mobile */
  @HostListener('window:scroll')
  onWindowScroll(): void {
    if (this.menuOpen()) return;
    if (this.anyOverlayOpen()) {
      // Lock: keep the nav hidden while any overlay is active and keep lastScrollY
      // current so reveal-logic starts from the correct baseline after dismissal.
      this.bottomNavHidden.set(true);
      this.lastScrollY = window.scrollY;
      return;
    }
    const currentY = window.scrollY;
    const delta = currentY - this.lastScrollY;
    if (Math.abs(delta) < App.SCROLL_DELTA_THRESHOLD) return;
    if (currentY <= App.SCROLL_TOP_THRESHOLD) {
      this.bottomNavHidden.set(false);
    } else if (delta > 0) {
      this.bottomNavHidden.set(true);
    } else {
      this.bottomNavHidden.set(false);
    }
    this.lastScrollY = currentY;
  }

  /**
   * Touch-move guard: if any overlay is open, force the bottom nav hidden.
   * This catches the case where scroll events are not fired (e.g. overscroll
   * or rubber-banding on iOS) but the user is still swiping.
   */
  @HostListener('window:touchmove')
  @HostListener('window:touchstart')
  onWindowTouch(): void {
    if (this.anyOverlayOpen()) {
      this.bottomNavHidden.set(true);
      this.lastScrollY = window.scrollY;
    }
  }

  /** Close notification center and lang dropdown when clicking anywhere outside */
  @HostListener('document:click')
  onDocumentClick(): void {
    this.notifCenterOpen.set(false);
    this.langDropdownOpen.set(false);
  }

  /** Global Escape key handler — closes the topmost active overlay (PC accessibility). */
  @HostListener('document:keydown.escape')
  onGlobalEscape(): void {
    // Synopsis and disabled-provider modals are higher-z layers — dismiss first.
    if (this.popupService.synopsisTitle()) {
      this.dismissSynopsisModal();
      return;
    }
    if (this.popupService.disabledProvider()) {
      this.dismissDisabledProviderModal();
      return;
    }
    // Notification center / lang dropdown
    if (this.notifCenterOpen()) {
      this.notifCenterOpen.set(false);
      return;
    }
    if (this.langDropdownOpen()) {
      this.langDropdownOpen.set(false);
      return;
    }
    // Side drawer (mobile)
    if (this.menuOpen()) {
      this.closeMenu();
      return;
    }
    // LuckyPick and FranchiseConstellation handle their own Escape listeners.
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
    this.popupService.openDisabledProvider(provider);
  }

  /** Opens the Plex home URL using native deep linking on mobile (with web fallback). */
  openPlexHomeLink(event: Event): void {
    event.preventDefault();
    this.plexDeepLink.openLink(this.plexHomeUrl());
  }

  dismissDisabledProviderModal(): void {
    this.popupService.dismissDisabledProvider();
  }

  dismissSynopsisModal(): void {
    this.popupService.dismissSynopsis();
  }

  goToProviderSettings(provider: 'plex' | 'sonarr'): void {
    this.popupService.dismissDisabledProvider();
    // Navigate after the exit animation completes.
    setTimeout(() => {
      // Navigate with a `highlight` query param so the Settings page can call
      // scrollToSection() — which handles offset-aware scroll, highlight scale, and auto-clear.
      this.router.navigate(['/settings'], { queryParams: { highlight: provider } });
    }, PopupService.MODAL_LEAVE_MS);
  }

  /** Convenience method for template — dismisses modal and navigates to provider settings. */
  dismissAndGoToSettings(): void {
    const provider = this.popupService.disabledProvider();
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

  switchLanguage(code?: string): void {
    const next = code ?? (this.currentLang() === 'en' ? 'fr' : 'en');
    this.currentLang.set(next);
    this.translate.use(next);
    this.langDropdownOpen.set(false);
  }

  toggleLangDropdown(event: Event): void {
    event.stopPropagation();
    this.langDropdownOpen.update((v) => !v);
  }

  closeLangDropdown(): void {
    this.langDropdownOpen.set(false);
  }

  /** Called when the user clicks × on a notification-center item. */
  onNotifDismiss(id: string, event: MouseEvent): void {
    event.stopPropagation();
    this.notifService.dismissNotifItem(id);
  }
}

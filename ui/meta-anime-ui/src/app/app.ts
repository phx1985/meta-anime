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
import { SeiyuuNexusComponent } from './seiyuu-nexus/seiyuu-nexus.component';
import { MalQuickUpdateModalComponent } from './mal-quick-update/mal-quick-update-modal.component';
import { ProviderHealthComponent } from './provider-health/provider-health.component';
import { AuthService } from './services/auth.service';
import { StorageKeys } from './constants/storage-keys';
import { UserPreferencesService } from './services/user-preferences.service';
import { HealthService } from './services/health.service';
import { ProviderHealthResult } from './models/health.models';
import { BotKunComponent } from './bot-kun/bot-kun.component';
import { AnimeDetailPanelComponent } from './anime-detail-panel/anime-detail-panel.component';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, LuckyPickComponent, UpperCasePipe, FranchiseConstellationComponent, SeiyuuNexusComponent, MalQuickUpdateModalComponent, ProviderHealthComponent, BotKunComponent, AnimeDetailPanelComponent],
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
  private authService = inject(AuthService);
  private userPrefs = inject(UserPreferencesService);
  private readonly healthService = inject(HealthService);

  /** Health signals for the mobile burger-menu drawer dots */
  protected readonly drawerPlexHealth = computed(() => this.healthService.snapshot()?.plex ?? null);
  protected readonly drawerSonarrHealth = computed(() => this.healthService.snapshot()?.sonarr ?? null);
  protected readonly drawerMalHealth = computed(() => this.healthService.snapshot()?.mal ?? null);

  protected drawerDotClass(result: ProviderHealthResult | null): string {
    if (!result || result.status === 'Unknown') return 'ph-dot dot--unknown';
    switch (result.status) {
      case 'Online':   return 'ph-dot dot--online';
      case 'Degraded': return 'ph-dot dot--degraded';
      case 'Offline':  return 'ph-dot dot--offline';
      default:         return 'ph-dot dot--unknown';
    }
  }

  @ViewChild('navbarEl', { read: ElementRef }) private navbarEl?: ElementRef<HTMLElement>;

  protected currentLang = signal<string>(localStorage.getItem(StorageKeys.language) || 'en');
  protected menuOpen = signal<boolean>(false);
  /** True during the drawer close animation (keeps the DOM alive while it slides out). */
  protected drawerClosing = signal<boolean>(false);
  protected notifCenterOpen = signal<boolean>(false);
  /** Dynamic top offset for the toast stack — tracks the actual navbar height */
  protected toastTopPx = signal<number>(72); // ~4.5rem default

  /** Controls the Lucky Pick overlay visibility */
  protected luckyPickOpen = signal<boolean>(false);
  /** Prevent rapid-fire clicks from re-opening while already open */
  private luckyPickBusy = false;

  /** Bot-kun: MAL ID of the anime to open in the detail panel (set when an anime link is clicked) */
  protected botKunSelectedMalId = signal<string | null>(null);

  /** Opens the AnimeDetailPanel for the MAL ID emitted by Bot-kun */
  onBotKunAnimeSelected(malId: string): void {
    this.botKunSelectedMalId.set(malId);
  }

  closeBotKunDetailPanel(): void {
    this.botKunSelectedMalId.set(null);
  }

  /** Lucky Pick: internal anime ID to open in the detail panel (on image/see-more click) */
  protected luckyPickDetailAnimeId = signal<string | null>(null);

  openLuckyPickDetail(animeId: string): void {
    this.luckyPickDetailAnimeId.set(animeId);
  }

  closeLuckyPickDetail(): void {
    this.luckyPickDetailAnimeId.set(null);
  }

  /** Explore Universe: MAL ID to open in the detail panel (on graph node click) */
  protected constellationDetailMalId = signal<string | null>(null);

  openConstellationDetail(malId: string): void {
    this.constellationDetailMalId.set(malId);
  }

  closeConstellationDetail(): void {
    this.constellationDetailMalId.set(null);
  }

  /** True when a JWT token is stored — controls logout button visibility. */
  protected isAuthenticated = computed(() => this.authService.isAuthenticated());

  /** Available languages for the dropdown */
  protected readonly availableLanguages: ReadonlyArray<{ code: string; label: string }> = [
    { code: 'en', label: 'English' },
    { code: 'fr', label: 'Français' },
  ];
  /** Blob indices for the global bokeh background (20 particles) */
  protected readonly bokehBlobs = Array.from({ length: 20 }, (_, i) => i + 1);
  /** True while the language dropdown is open */
  protected langDropdownOpen = signal<boolean>(false);

  /** Provider home-page URLs (populated from settings) */
  protected plexHomeUrl = signal<string | null>(null);
  protected sonarrHomeUrl = signal<string | null>(null);
  protected readonly malHomeUrl = 'https://myanimelist.net';

  /** True while the top navbar and bottom mobile nav must be hidden (wizard or login routes) */
  protected hideNavRoute = signal<boolean>(false);
  /** True while the bottom nav bar is retracted (scroll-down hide on mobile) */
  protected bottomNavHidden = signal<boolean>(false);
  /** True whenever any overlay / bottom-sheet / modal is open. */
  protected anyOverlayOpen = computed(
    () =>
      this.luckyPickOpen() ||
      this.menuOpen() ||
      this.drawerClosing() ||
      this.popupService.childOverlayOpen() ||
      this.popupService.addProviderModalOpen() ||
      this.popupService.franchiseConstellationOpen() ||
      !!this.popupService.disabledProvider() ||
      !!this.popupService.synopsisTitle() ||
      !!this.popupService.syncPromptProvider() ||
      !!this.popupService.malQuickUpdate(),
  );
  /** i18n key of the active route – displayed as a page label in the top header on mobile */
  protected currentPageKey = signal<string>('');
  /** Tracks the last scroll position for scroll-direction detection */
  private lastScrollY = 0;
  /** Minimum scroll delta (px) before direction is detected */
  private static readonly SCROLL_DELTA_THRESHOLD = 4;
  /** If scroll position is at or below this many px from the top, always reveal the nav */
  private static readonly SCROLL_TOP_THRESHOLD = 40;

  /** Route → nav i18n key mapping */
  private static readonly PAGE_KEYS: Record<string, string> = {
    '/animes': 'nav.animes',
    '/seasonal': 'nav.seasonal',
    '/mal-search': 'nav.malSearch',
    '/settings': 'nav.settings',
    '/library': 'nav.library',
  };

  /** Controls the disabled-provider interception modal — delegated to PopupService */
  protected get disabledProviderModal() { return this.popupService.disabledProvider; }
  protected get modalLeaving() { return this.popupService.disabledProviderLeaving; }

  /** Controls the synopsis modal — delegated to PopupService */
  protected get synopsisModal() { return this.popupService.synopsisTitle; }
  protected get synopsisBody() { return this.popupService.synopsisBody; }
  protected get synopsisLeaving() { return this.popupService.synopsisLeaving; }

  constructor() {
    this.translate.use(localStorage.getItem(StorageKeys.language) || App.DEFAULT_LANG);
  }

  ngOnInit(): void {
    // Immediately log out if stored JWT has expired, then check every 60 s
    this.authService.checkAndLogoutIfExpired();
    const expiryCheckId = setInterval(() => {
      if (this.authService.checkAndLogoutIfExpired()) {
        this.router.navigate(['/login']);
      }
    }, 60_000);
    this.destroyRef.onDestroy(() => clearInterval(expiryCheckId));

    // Connect to SignalR hub globally so sync done/error notifications work on any page
    this.syncSignalR.connect();

    // Track active route for the mobile page-context label
    let prevWasWizard = false;
    // Initialise nav-hide signal synchronously before any NavigationEnd fires
    const initialPath = '/' + this.router.url.replace(/^\//, '').split('?')[0].split('#')[0].split('/')[0];
    this.hideNavRoute.set(initialPath === '/welcome' || initialPath === '/login');
    prevWasWizard = initialPath === '/welcome';

    this.router.events
      .pipe(
        filter((e): e is NavigationEnd => e instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((e) => {
        // Strip query params and hash, then map to a page i18n key ('' for unmapped routes)
        const path = '/' + e.urlAfterRedirects.replace(/^\//, '').split('?')[0].split('#')[0].split('/')[0];
        this.currentPageKey.set(App.PAGE_KEYS[path] ?? '');
        this.hideNavRoute.set(path === '/welcome' || path === '/login');
        // Always reveal the bottom nav when navigating to a new page
        this.bottomNavHidden.set(false);
        this.lastScrollY = 0;
        // When leaving the wizard, refresh provider URLs and state so nav links appear instantly
        if (prevWasWizard && path !== '/welcome') {
          this.loadProviderUrls();
          this.providerState.refresh();
        }
        prevWasWizard = path === '/welcome';
      });

    // Sync done → success notification (fires on every transition false→true, not on replay)
    this.syncSignalR.syncDone$
      .pipe(
        filter((v) => v),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        this.notifService.pushOrReplace({
          id: 'sync-done',
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

    // Push "Welcome" notification once ever, persisted server-side so it
    // doesn't re-appear when localStorage is cleared.
    // isWelcomeShown() returns null if load() failed (auth issue) — skip the
    // welcome in that case rather than incorrectly re-showing it.
    if (this.userPrefs.isWelcomeShown() === false) {
      this.notifService.push({
        id: 'welcome-v2',
        severity: 'info',
        titleKey: 'notifications.welcome.title',
        messageKey: 'notifications.welcome.message',
      });
      this.userPrefs.markWelcomeShown();
    }

    // Load provider URLs for navbar quick-links
    this.loadProviderUrls();
  }

  /** Load/refresh provider home-page URLs from saved settings */
  private loadProviderUrls(): void {
    this.settingsService.getAll().subscribe({
      next: (settings) => {
        const plexHost = settings.find((s) => s.key === 'Plex:Host')?.value ?? null;
        const sonarrUrl =
          settings.find((s) => s.key === 'Sonarr:RemoteUrl')?.value ||
          settings.find((s) => s.key === 'Sonarr:Host')?.value ||
          null;
        if (plexHost) this.plexHomeUrl.set('https://app.plex.tv');
        if (sonarrUrl) this.sonarrHomeUrl.set(sonarrUrl);

        // Push the Bot-kun discovery notification once if it has never been shown
        const botkunEnabled = settings.find((s) => s.key === 'BotKun:Enabled')?.value === 'true';
        if (!botkunEnabled && !this.notifService.has('botkun-discover')) {
          this.notifService.push({
            id: 'botkun-discover',
            severity: 'info',
            titleKey: 'notifications.botKun.discoverTitle',
            messageKey: 'notifications.botKun.discoverMessage',
            action: {
              labelKey: 'notifications.botKun.configure',
              route: ['/settings'],
              fragment: 'section-botkun',
            },
          });
        }
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
    if (this.popupService.malQuickUpdate()) {
      this.popupService.dismissMalQuickUpdate();
      return;
    }
    if (this.popupService.synopsisTitle()) {
      this.dismissSynopsisModal();
      return;
    }
    if (this.popupService.disabledProvider()) {
      this.dismissDisabledProviderModal();
      return;
    }
    // Settings page sync-prompt modal
    if (this.popupService.syncPromptProvider()) {
      this.popupService.dismissSyncPrompt();
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
    setTimeout(() => (this.luckyPickBusy = false), App.LUCKY_PICK_GUARD_MS);
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
    if (diff < App.MS_PER_MINUTE) return this.translate.instant('notifications.time.justNow');
    if (diff < App.MS_PER_HOUR)
      return this.translate.instant('notifications.time.minutesAgo', {
        n: Math.floor(diff / App.MS_PER_MINUTE),
      });
    if (diff < App.MS_PER_DAY)
      return this.translate.instant('notifications.time.hoursAgo', {
        n: Math.floor(diff / App.MS_PER_HOUR),
      });
    return this.translate.instant('notifications.time.daysAgo', {
      n: Math.floor(diff / App.MS_PER_DAY),
    });
  }

  private static readonly MS_PER_MINUTE  = 60_000;
  private static readonly MS_PER_HOUR    = 3_600_000;
  private static readonly MS_PER_DAY     = 86_400_000;
  private static readonly LUCKY_PICK_GUARD_MS = 500;
  private static readonly DEFAULT_LANG        = 'en';

  /** Duration of the CSS `.navbar-collapse` max-height transition — keep in sync with app.scss */
  private static readonly NAVBAR_COLLAPSE_MS = 310;
  /** Duration of the drawer slide-out CSS animation (must match SCSS keyframe). */
  private static readonly DRAWER_CLOSE_ANIMATION_MS = 240;

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
    if (this.menuOpen()) {
      this.closeMenu(); // plays the slide-out animation
    } else {
      this.menuOpen.set(true);
      setTimeout(() => this.syncToastTop(), App.NAVBAR_COLLAPSE_MS);
    }
  }

  closeMenu(): void {
    // Keep the DOM alive for the slide-out animation, then remove.
    this.drawerClosing.set(true);
    this.menuOpen.set(false);
    setTimeout(() => {
      this.drawerClosing.set(false);
      this.syncToastTop();
    }, App.DRAWER_CLOSE_ANIMATION_MS);
  }

  // ── Drawer swipe-to-close (right-swipe closes the right-side nav drawer) ──
  private drawerTouchStartX = 0;

  @HostListener('window:touchstart', ['$event'])
  onDrawerTouchStart(event: TouchEvent): void {
    if (!this.menuOpen()) return;
    this.drawerTouchStartX = event.touches[0].clientX;
  }

  @HostListener('window:touchend', ['$event'])
  onDrawerTouchEnd(event: TouchEvent): void {
    if (!this.menuOpen()) return;
    const deltaX = event.changedTouches[0].clientX - this.drawerTouchStartX;
    // Right-swipe ≥ 60 px dismisses the drawer (slides it back to the right edge)
    if (deltaX >= 60) this.closeMenu();
  }

  switchLanguage(code?: string): void {
    const next = code ?? (this.currentLang() === 'en' ? 'fr' : 'en');
    this.currentLang.set(next);
    this.translate.use(next);
    localStorage.setItem(StorageKeys.language, next);
    this.langDropdownOpen.set(false);
  }

  toggleLangDropdown(event: Event): void {
    event.stopPropagation();
    this.langDropdownOpen.update((v) => !v);
  }

  closeLangDropdown(): void {
    this.langDropdownOpen.set(false);
  }

  /** Logs out the current admin session and navigates to the login page. */
  logout(): void {
    this.authService.logout();
    this.router.navigate(['/login']);
  }

  /** Called when the user clicks × on a notification-center item. */
  onNotifDismiss(id: string, event: MouseEvent): void {
    event.stopPropagation();
    this.notifService.dismissNotifItem(id);
  }
}

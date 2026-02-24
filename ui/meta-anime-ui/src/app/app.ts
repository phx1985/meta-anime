import { Component, inject, signal, OnInit, HostListener, DestroyRef, ViewChild, ElementRef, AfterViewInit, effect } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { filter } from 'rxjs/operators';
import { ThemeService } from './services/theme.service';
import { SettingsService } from './services/settings.service';
import { NotificationService, AppNotification } from './services/notification.service';
import { SyncSignalRService } from './services/sync-signalr.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit, AfterViewInit {
  protected themeService    = inject(ThemeService);
  protected translate       = inject(TranslateService);
  protected notifService    = inject(NotificationService);
  private   settingsService = inject(SettingsService);
  private   syncSignalR     = inject(SyncSignalRService);
  private   router          = inject(Router);
  private   destroyRef      = inject(DestroyRef);

  @ViewChild('navbarEl', { read: ElementRef }) private navbarEl?: ElementRef<HTMLElement>;

  protected currentLang     = signal<string>('en');
  protected menuOpen        = signal<boolean>(false);
  protected notifCenterOpen = signal<boolean>(false);
  /** Dynamic top offset for the toast stack — tracks the actual navbar height */
  protected toastTopPx      = signal<number>(72); // ~4.5rem default

  /** Provider home-page URLs (populated from settings) */
  protected plexHomeUrl   = signal<string | null>(null);
  protected sonarrHomeUrl = signal<string | null>(null);
  protected readonly malHomeUrl = 'https://myanimelist.net';

  /**
   * Duration of the JS pixel animation container cleanup.
   * Must be ≤ TOAST_EXIT_MS (820ms) so the original DOM element is removed
   * by the service before this cleans up the overlay; 20ms buffer for safety.
   */
  private static readonly PIXEL_ANIM_MS = 800;
  /**
   * Delay (ms) before collapsing a notif-item's DOM slot after pixel animation.
   * Chosen to let ~70% of blocks fly away before the height collapses,
   * which feels natural. Must be < NOTIF_DISMISS_MS (750ms).
   */
  private static readonly NOTIF_PIXEL_SETTLE_MS = 350;
  /** IDs for which a pixel disintegration is already running (dedup guard). */
  private readonly _animatingPixelIds = new Set<string>();

  // ── Pixel grid sizing ────────────────────────────────────────────────────────
  /** Minimum pixel block columns (used for tall-but-narrow elements). */
  private static readonly PX_MIN_COLS = 8;
  /** Minimum pixel block rows (used for short elements). */
  private static readonly PX_MIN_ROWS = 4;
  /** Target pixel block width (px). Grid density = element_width / this. */
  private static readonly PX_BLOCK_TARGET_W = 22;
  /** Target pixel block height (px). Grid density = element_height / this. */
  private static readonly PX_BLOCK_TARGET_H = 16;

  // ── Stagger / duration ───────────────────────────────────────────────────────
  /** Per-column delay increment (ms) for the right-to-left stagger. */
  private static readonly PX_COL_STAGGER_MS  = 18;
  /** Per-row delay increment (ms). Rows leave in waves from top to bottom. */
  private static readonly PX_ROW_STAGGER_MS  = 10;
  /** Random jitter added to each block's delay (ms). */
  private static readonly PX_DELAY_JITTER_MS = 35;
  /** Minimum flight duration per block (ms). */
  private static readonly PX_DURATION_MIN_MS = 480;
  /** Random additional duration per block (ms). */
  private static readonly PX_DURATION_JITTER  = 260;

  // ── Block physics ────────────────────────────────────────────────────────────
  /** Half-angle of the fan spread (radians). Blocks fan ±45% of π around 0°. */
  private static readonly PX_SPREAD_HALF_RAD  = Math.PI * 0.45;
  /** Minimum flight speed (px). */
  private static readonly PX_SPEED_MIN        = 90;
  /** Random extra flight speed (px). */
  private static readonly PX_SPEED_JITTER     = 200;
  /** Constant rightward bias added to dx (px), emphasises Thanos direction. */
  private static readonly PX_RIGHTWARD_BIAS   = 20;
  /** Base downward bias added to dy before per-row gravity (px). */
  private static readonly PX_GRAVITY_BASE     = 35;
  /** Per-row gravity increment (px/row). Lower rows fall faster. */
  private static readonly PX_GRAVITY_PER_ROW  = 14;
  /** Maximum total rotation (±360°). */
  private static readonly PX_MAX_ROT_DEG      = 720;
  /** Minimum final scale (blocks shrink as they fly). */
  private static readonly PX_FINAL_SCALE_MIN  = 0.04;
  /** Random extra final scale. */
  private static readonly PX_FINAL_SCALE_JITTER = 0.14;

  constructor() {
    this.translate.use('en');

    // Pixel disintegration for every toast that enters "leaving" state.
    // The CSS .toast-item--leaving just sets opacity:0 immediately so the
    // real DOM element vanishes; we recreate it as flying pixel blocks.
    effect(() => {
      const leaving = this.notifService.leavingToastIds();
      leaving.forEach(id => {
        if (this._animatingPixelIds.has(id)) return;
        this._animatingPixelIds.add(id);
        const el = document.querySelector(`[data-toast-id="${CSS.escape(id)}"]`) as HTMLElement | null;
        if (el) this._pixelDisintegrate(el);
      });
      if (leaving.size === 0) this._animatingPixelIds.clear();
    });
  }

  ngOnInit(): void {
    // Connect to SignalR hub globally so sync done/error notifications work on any page
    this.syncSignalR.connect();

    // Sync done → success notification (fires on every transition false→true, not on replay)
    this.syncSignalR.syncDone$
      .pipe(filter(v => v), takeUntilDestroyed(this.destroyRef))
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
      .pipe(filter(v => v), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.notifService.push({
          id: `sync-error-${Date.now()}`,
          severity: 'error',
          titleKey: 'notifications.sync.errorTitle',
          messageKey: 'notifications.sync.errorMessage',
          action: { labelKey: 'notifications.sync.viewLog', route: ['/settings'], fragment: 'sync-section' },
        });
      });

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
    this.menuOpen.update(v => !v);
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

  /**
   * Called when the user clicks the × button on a notification-center item.
   * Runs the pixel disintegration on the item element, then after a short
   * delay tells the service to collapse the DOM slot (height → 0).
   */
  onNotifDismiss(id: string, event: MouseEvent): void {
    event.stopPropagation();
    if (this._animatingPixelIds.has(id)) return;
    this._animatingPixelIds.add(id);
    const btn = event.currentTarget as HTMLElement;
    const item = btn.closest('.notif-item') as HTMLElement | null;
    if (item) this._pixelDisintegrate(item);
    // Wait for pixel blocks to visually depart before the service collapses height
    setTimeout(() => {
      this.notifService.dismissNotifItem(id);
      this._animatingPixelIds.delete(id);
    }, App.NOTIF_PIXEL_SETTLE_MS);
  }

  /**
   * Thanos-snap pixel block disintegration.
   *
   * Creates a grid of small `div` blocks positioned over `el`, immediately
   * hides `el` (opacity:0), then animates each block flying outward using
   * the Web Animations API.  Blocks stagger from right-to-left (imitating
   * the Thanos snap where the right side crumbles first).
   */
  private _pixelDisintegrate(el: HTMLElement): void {
    const rect = el.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) return;

    const COLS  = Math.max(App.PX_MIN_COLS, Math.round(rect.width  / App.PX_BLOCK_TARGET_W));
    const ROWS  = Math.max(App.PX_MIN_ROWS, Math.round(rect.height / App.PX_BLOCK_TARGET_H));
    const blockW = rect.width  / COLS;
    const blockH = rect.height / ROWS;

    // Capture element's background for block colour; fallback to card variable
    const bgColor = getComputedStyle(el).backgroundColor || 'var(--card-bg)';

    // Hide the real element immediately — the JS overlay IS the animation
    el.style.setProperty('opacity', '0', 'important');
    el.style.setProperty('transition', 'none', 'important');

    const container = document.createElement('div');
    container.style.cssText =
      `position:fixed;left:${rect.left}px;top:${rect.top}px;` +
      `width:${rect.width}px;height:${rect.height}px;` +
      `pointer-events:none;z-index:999999;overflow:visible;`;

    for (let row = 0; row < ROWS; row++) {
      for (let col = 0; col < COLS; col++) {
        const block = document.createElement('div');

        // Right-to-left stagger: rightmost column leaves first (Thanos snap)
        const delay    = (COLS - 1 - col) * App.PX_COL_STAGGER_MS
                       + row              * App.PX_ROW_STAGGER_MS
                       + Math.random()    * App.PX_DELAY_JITTER_MS;
        const duration = App.PX_DURATION_MIN_MS + Math.random() * App.PX_DURATION_JITTER;

        // Direction: fan spread ± PX_SPREAD_HALF_RAD around 0° (rightward)
        const angle      = (Math.random() - 0.5) * 2 * App.PX_SPREAD_HALF_RAD;
        const speed      = App.PX_SPEED_MIN + Math.random() * App.PX_SPEED_JITTER;
        const dx         = Math.cos(angle) * speed + App.PX_RIGHTWARD_BIAS;
        const dy         = Math.sin(angle) * speed + App.PX_GRAVITY_BASE + row * App.PX_GRAVITY_PER_ROW;
        const rot        = (Math.random() - 0.5) * App.PX_MAX_ROT_DEG;
        const finalScale = App.PX_FINAL_SCALE_MIN + Math.random() * App.PX_FINAL_SCALE_JITTER;

        block.style.cssText =
          `position:absolute;` +
          `left:${col * blockW}px;top:${row * blockH}px;` +
          // +0.5px prevents sub-pixel rendering gaps between adjacent blocks
          `width:${blockW + 0.5}px;height:${blockH + 0.5}px;` +
          `background:${bgColor};` +
          `box-shadow:inset 0 0 0 0.5px rgba(255,255,255,0.08);` +
          `border-radius:1px;`;

        block.animate(
          [
            { opacity: 1,   transform: 'translate(0,0) rotate(0deg) scale(1)',
              filter: 'brightness(1)' },
            { opacity: 0.9, transform: `translate(${dx * .18}px,${dy * .09}px) rotate(${rot * .15}deg) scale(1.04)`,
              filter: 'brightness(1.8) saturate(2)', offset: 0.18 },
            { opacity: 0,   transform: `translate(${dx}px,${dy}px) rotate(${rot}deg) scale(${finalScale})`,
              filter: 'brightness(0.5) saturate(0) blur(2px)' },
          ],
          { duration, delay, easing: 'cubic-bezier(0.4,0,1,1)', fill: 'forwards' }
        );

        container.appendChild(block);
      }
    }

    document.body.appendChild(container);
    setTimeout(() => {
      if (document.body.contains(container)) document.body.removeChild(container);
    }, App.PIXEL_ANIM_MS);
  }
}


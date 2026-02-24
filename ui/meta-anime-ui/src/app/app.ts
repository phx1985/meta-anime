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
   * Total duration of the JS pixel animation container (ms).
   * = PX_MAX_STAGGER_MS + max(PX_DURATION_MIN_MS + PX_DURATION_JITTER) + PX_DELAY_JITTER_MS + buffer
   */
  private static readonly PIXEL_ANIM_MS = 1400;
  /**
   * Delay (ms) before collapsing a notif-item's DOM slot after pixel animation.
   * Must be ≥ PX_MAX_STAGGER_MS + PX_DURATION_MIN_MS + PX_DURATION_JITTER
   * so all particles have finished their opacity fade (Phase 5 of the spec).
   */
  private static readonly NOTIF_PIXEL_SETTLE_MS = 1310;
  /** IDs for which a pixel disintegration is already running (dedup guard). */
  private readonly _animatingPixelIds = new Set<string>();

  // ── Pixel grid sizing ────────────────────────────────────────────────────────
  /** Minimum pixel block columns. */
  private static readonly PX_MIN_COLS = 8;
  /** Minimum pixel block rows. */
  private static readonly PX_MIN_ROWS = 4;
  /** Target pixel block width (px). */
  private static readonly PX_BLOCK_TARGET_W = 8;
  /** Target pixel block height (px). */
  private static readonly PX_BLOCK_TARGET_H = 6;

  // ── Stagger (Phase 2) ────────────────────────────────────────────────────────
  /**
   * Maximum right-to-left stagger delay (ms).
   * Rightmost column = 0 ms delay; leftmost column = this value.
   * Delay formula: delay = (1 - colNorm) * PX_MAX_STAGGER_MS + jitter
   */
  private static readonly PX_MAX_STAGGER_MS = 600;
  /** Random per-block delay jitter (ms) added on top of the stagger. */
  private static readonly PX_DELAY_JITTER_MS = 30;
  /** Minimum flight duration per block (ms). */
  private static readonly PX_DURATION_MIN_MS = 380;
  /** Random additional flight duration per block (ms). */
  private static readonly PX_DURATION_JITTER = 300;

  // ── Parabolic trajectory (Phase 3) ───────────────────────────────────────────
  /**
   * Fraction of the animation timeline at which a block reaches its apex.
   * 0.25 = the upward burst peaks at 25% of the total flight duration.
   */
  private static readonly PX_PEAK_FRACTION = 0.25;
  /** Minimum upward burst height (px). The block rises this far at peak. */
  private static readonly PX_BURST_UP_MIN = 18;
  /** Random additional upward burst (px). */
  private static readonly PX_BURST_UP_JITTER = 28;
  /** Half-angle of horizontal fan spread (rad). Blocks spread ±this from rightward. */
  private static readonly PX_SPREAD_HALF_RAD = 0.28;
  /** Minimum horizontal speed (px over full duration). */
  private static readonly PX_SPEED_MIN = 60;
  /** Random extra horizontal speed (px). */
  private static readonly PX_SPEED_JITTER = 180;
  /** Constant rightward bias added to horizontal movement (px). */
  private static readonly PX_RIGHTWARD_BIAS = 15;
  /** Base downward drop for the top rows (px). */
  private static readonly PX_GRAVITY_BASE = 55;
  /** Additional downward drop per row (px/row). Lower rows fall further. */
  private static readonly PX_GRAVITY_PER_ROW = 8;
  /** Maximum total rotation per block (±deg). */
  private static readonly PX_MAX_ROT_DEG = 540;
  /** Fraction of total rotation applied at the trajectory apex. */
  private static readonly PX_PEAK_ROT_FRACTION = 0.08;

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
   * Spec-compliant pixel-block disintegration (Thanos-snap, right-to-left wave).
   *
   * Phase 1  — Seamless overlay: build the full particle grid off-DOM first.
   *            Once complete, atomically hide the original element (visibility:hidden
   *            preserves its layout space) and attach the grid. Frame 0 is invisible.
   *
   * Phase 2  — Right-to-left stagger: rightmost column starts at 0 ms delay,
   *            leftmost at PX_MAX_STAGGER_MS (600 ms). Delay is strictly proportional
   *            to normalised X position so the disintegration sweeps as a smooth wave.
   *
   * Phase 3  — Parabolic physics: each block bursts slightly upward (initial velocity),
   *            reaches a peak at PX_PEAK_FRACTION of its flight time, then gravity
   *            accelerates it downward.  Horizontal travel is rightward with a fan spread.
   *            The easing on each keyframe reflects physical momentum:
   *              0% → peak : ease-out (fast burst, slowing at apex)
   *              peak → 100%: ease-in  (slow at apex, accelerating under gravity)
   *
   * Phase 4  — Smooth dissolve: scale 1→0 and opacity 1→0 continuously throughout
   *            the parabolic flight so particles organically shrink into nothingness.
   *
   * Phase 5  — Graceful DOM collapse: the original element (visibility:hidden) keeps
   *            its exact height in layout while all blocks fly.  The caller is
   *            responsible for invoking the service collapse method after
   *            NOTIF_PIXEL_SETTLE_MS (≥ max stagger + max duration) so that the
   *            final leftmost particle has fully faded before the height collapses.
   */
  private _pixelDisintegrate(el: HTMLElement): void {
    const rect = el.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) return;

    const COLS   = Math.max(App.PX_MIN_COLS, Math.round(rect.width  / App.PX_BLOCK_TARGET_W));
    const ROWS   = Math.max(App.PX_MIN_ROWS, Math.round(rect.height / App.PX_BLOCK_TARGET_H));
    const blockW = rect.width  / COLS;
    const blockH = rect.height / ROWS;

    // Sample the element's computed background as block fill colour.
    const bgColor = getComputedStyle(el).backgroundColor || 'var(--card-bg)';

    // Build the full particle grid completely off-DOM (Phase 1).
    const container = document.createElement('div');
    container.style.cssText =
      `position:fixed;left:${rect.left}px;top:${rect.top}px;` +
      `width:${rect.width}px;height:${rect.height}px;` +
      `pointer-events:none;z-index:999999;overflow:visible;`;

    for (let row = 0; row < ROWS; row++) {
      for (let col = 0; col < COLS; col++) {
        const block = document.createElement('div');

        // Phase 2: Right-to-left stagger.
        // colNorm: 0 = leftmost column (max delay), 1 = rightmost (0 ms delay).
        // When COLS === 1 treat as rightmost so single-column elements start immediately.
        const colNorm  = COLS > 1 ? col / (COLS - 1) : 1;
        const delay    = (1 - colNorm) * App.PX_MAX_STAGGER_MS
                       + Math.random() * App.PX_DELAY_JITTER_MS;
        const duration = App.PX_DURATION_MIN_MS + Math.random() * App.PX_DURATION_JITTER;

        // Phase 3: Parabolic trajectory.
        const upBurst  = App.PX_BURST_UP_MIN  + Math.random() * App.PX_BURST_UP_JITTER;
        const angle    = (Math.random() - 0.5) * 2 * App.PX_SPREAD_HALF_RAD;
        const speed    = App.PX_SPEED_MIN + Math.random() * App.PX_SPEED_JITTER;
        const dxFull   = Math.cos(angle) * speed + App.PX_RIGHTWARD_BIAS;
        const dyFall   = App.PX_GRAVITY_BASE + row * App.PX_GRAVITY_PER_ROW;
        // At the peak the block has travelled PX_PEAK_FRACTION of its full X distance.
        const dxPeak   = dxFull * App.PX_PEAK_FRACTION;
        const rot      = (Math.random() - 0.5) * App.PX_MAX_ROT_DEG;

        block.style.cssText =
          `position:absolute;` +
          `left:${col * blockW}px;top:${row * blockH}px;` +
          // +0.5 px plugs sub-pixel gaps between adjacent blocks
          `width:${blockW + 0.5}px;height:${blockH + 0.5}px;` +
          `background:${bgColor};` +
          `box-shadow:inset 0 0 0 0.5px rgba(255,255,255,0.08);` +
          `border-radius:1px;`;

        block.animate(
          [
            // 0% — at rest, full size and opacity.
            // ease-out: burst starts fast and decelerates toward the apex.
            { offset: 0,
              opacity: 1,
              transform: 'translate(0,0) rotate(0deg) scale(1)',
              easing: 'cubic-bezier(0.25, 1, 0.5, 1)' },
            // peak — upward burst + partial rightward travel; slight brightness flash.
            // ease-in: slow at apex, then gravity accelerates the block downward.
            { offset: App.PX_PEAK_FRACTION,
              opacity: 0.82,
              transform: `translate(${dxPeak}px,${-upBurst}px) rotate(${rot * App.PX_PEAK_ROT_FRACTION}deg) scale(1)`,
              filter: 'brightness(1.25)',
              easing: 'cubic-bezier(0.5, 0, 0.75, 0)' },
            // 100% — gravity has pulled the block down; fully faded and shrunk (Phase 4).
            { offset: 1.0,
              opacity: 0,
              transform: `translate(${dxFull}px,${dyFall}px) rotate(${rot}deg) scale(0)`,
              filter: 'brightness(0.4) saturate(0) blur(0.5px)' },
          ],
          { duration, delay, fill: 'forwards' }
        );

        container.appendChild(block);
      }
    }

    // Phase 1 (spec): particle grid is fully built — now atomically:
    //   1. Hide the real element; visibility:hidden preserves its layout space (Phase 5).
    //   2. Attach the pixel overlay at the exact viewport position.
    // Both happen in the same JS microtask so the browser paints them together,
    // resulting in zero visible change at frame 0.
    el.style.setProperty('visibility', 'hidden', 'important');
    document.body.appendChild(container);

    setTimeout(() => {
      if (document.body.contains(container)) document.body.removeChild(container);
    }, App.PIXEL_ANIM_MS);
  }
}


import {
  ChangeDetectionStrategy,
  Component,
  ViewEncapsulation,
  computed,
  inject,
  DestroyRef,
  signal,
  OnInit,
  effect,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ProviderStateService } from '../services/provider-state.service';
import { PopupService } from '../services/popup.service';
import { PlexDeepLinkService } from '../services/plex-deep-link.service';
import { SettingsService } from '../services/settings.service';
import { HealthService } from '../services/health.service';
import { NotificationService } from '../services/notification.service';
import { ProviderHealthResult, ProviderHealthStatus } from '../models/health.models';

@Component({
  selector: 'app-provider-health',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './provider-health.component.html',
  styleUrl: './provider-health.component.scss',
})
export class ProviderHealthComponent implements OnInit {
  private readonly providerState = inject(ProviderStateService);
  private readonly popupService = inject(PopupService);
  private readonly plexDeepLink = inject(PlexDeepLinkService);
  private readonly settingsService = inject(SettingsService);
  private readonly healthService = inject(HealthService);
  private readonly notifService = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  readonly malHomeUrl = 'https://myanimelist.net';

  protected plexHomeUrl = signal<string | null>(null);
  protected sonarrHomeUrl = signal<string | null>(null);
  protected malClientIdConfigured = signal<boolean>(false);

  protected readonly plexConfigured = computed(() => !!this.plexHomeUrl());
  protected readonly sonarrConfigured = computed(() => !!this.sonarrHomeUrl());
  protected readonly malConfigured = computed(() => this.malClientIdConfigured());

  protected readonly plexEnabled = computed(() => this.providerState.plexEnabled());
  protected readonly sonarrEnabled = computed(() => this.providerState.sonarrEnabled());

  protected readonly plexHealth = computed(() => this.healthService.snapshot()?.plex ?? null);
  protected readonly sonarrHealth = computed(() => this.healthService.snapshot()?.sonarr ?? null);
  protected readonly malHealth = computed(() => this.healthService.snapshot()?.mal ?? null);

  // Previous states for transition detection — plain fields, not signals, to avoid circular reactivity.
  private prevPlex: ProviderHealthStatus | null = null;
  private prevSonarr: ProviderHealthStatus | null = null;
  private prevMal: ProviderHealthStatus | null = null;

  constructor() {
    effect(() => {
      const snap = this.healthService.snapshot();
      if (!snap) return;
      this.checkTransition(snap.plex.status, 'plex');
      this.checkTransition(snap.sonarr.status, 'sonarr');
      this.checkTransition(snap.mal.status, 'mal');
    });
  }

  ngOnInit(): void {
    this.settingsService.getAll().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (settings) => {
        const plexHost = settings.find((s) => s.key === 'Plex:Host')?.value ?? null;
        const sonarrUrl =
          settings.find((s) => s.key === 'Sonarr:RemoteUrl')?.value ||
          settings.find((s) => s.key === 'Sonarr:Host')?.value ||
          null;
        const malClientId = settings.find((s) => s.key === 'MAL:ClientId')?.value ?? null;
        if (plexHost) this.plexHomeUrl.set('https://app.plex.tv');
        if (sonarrUrl) this.sonarrHomeUrl.set(sonarrUrl);
        this.malClientIdConfigured.set(!!malClientId);
      },
      error: () => { /* non-critical */ },
    });
  }

  protected openPlexHomeLink(event: Event): void {
    event.preventDefault();
    this.plexDeepLink.openLink(this.plexHomeUrl());
  }

  protected openDisabledProviderModal(provider: 'plex' | 'sonarr', event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.popupService.openDisabledProvider(provider);
  }

  /**
   * Returns the full CSS class string for a status dot, always including the
   * `ph-dot` base class so Angular's [class] binding does not wipe it.
   */
  protected dotClass(result: ProviderHealthResult | null): string {
    const modifier = this.dotModifier(result);
    return `ph-dot ${modifier}`;
  }

  protected tooltipText(name: string, result: ProviderHealthResult | null): string {
    if (!result || result.status === 'Unknown') return `${name} · Checking…`;
    const latency = result.latencyMs >= 1000
      ? `${(result.latencyMs / 1000).toFixed(1)}s`
      : `${result.latencyMs}ms`;
    const detail = result.message ? ` · ${result.message}` : ` · ${latency}`;
    return `${name} · ${result.status}${detail}`;
  }

  // ── Notification transition tracking ──────────────────────────────────────

  private dotModifier(result: ProviderHealthResult | null): string {
    if (!result || result.status === 'Unknown') return 'dot--unknown';
    switch (result.status) {
      case 'Online':   return 'dot--online';
      case 'Degraded': return 'dot--degraded';
      case 'Offline':  return 'dot--offline';
      default:         return 'dot--unknown';
    }
  }

  private checkTransition(
    current: ProviderHealthStatus,
    key: 'plex' | 'sonarr' | 'mal',
  ): void {
    if (current === 'Unknown') return;

    const prev = key === 'plex' ? this.prevPlex : key === 'sonarr' ? this.prevSonarr : this.prevMal;

    if (prev !== null && prev !== 'Unknown') {
      const nowFailing = current === 'Offline';
      const wasFailing = prev === 'Offline';

      if (nowFailing && !wasFailing) {
        this.notifService.push({
          id: `health-down-${key}-${Date.now()}`,
          severity: 'error',
          titleKey: `notifications.health.${key}.downTitle`,
          messageKey: `notifications.health.${key}.downMessage`,
        });
      } else if (!nowFailing && wasFailing) {
        this.notifService.push({
          id: `health-up-${key}-${Date.now()}`,
          severity: 'success',
          titleKey: `notifications.health.${key}.upTitle`,
          messageKey: `notifications.health.${key}.upMessage`,
        });
      }
    }

    if (key === 'plex')   this.prevPlex   = current;
    if (key === 'sonarr') this.prevSonarr = current;
    if (key === 'mal')    this.prevMal    = current;
  }
}


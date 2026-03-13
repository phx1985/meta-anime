import { Injectable, inject, signal, computed } from '@angular/core';
import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SettingsService } from './settings.service';

/**
 * Centralized service that exposes the enabled/disabled state of optional
 * providers (Plex, Sonarr, Bot-kun) as reactive signals. All components inject
 * this service instead of each loading settings independently.
 *
 * Default is `true` (enabled) so the UI renders normally before settings load.
 * Bot-kun defaults to `false` — requires explicit opt-in via BotKun:Enabled.
 */
@Injectable({ providedIn: 'root' })
export class ProviderStateService {
  private settingsService = inject(SettingsService);
  private destroyRef = inject(DestroyRef);

  private readonly _plexEnabled = signal<boolean>(true);
  private readonly _sonarrEnabled = signal<boolean>(true);
  private readonly _plexMachineId = signal<string>('');
  private readonly _botkunEnabled = signal<boolean>(false);
  private readonly _botkunBaseUrl = signal<string>('');
  private readonly _botkunModel = signal<string>('');

  /** Whether the Plex provider is enabled (default: true). */
  readonly plexEnabled = this._plexEnabled.asReadonly();
  /** Whether the Sonarr provider is enabled (default: true). */
  readonly sonarrEnabled = this._sonarrEnabled.asReadonly();
  /** The Plex server's MachineIdentifier (empty until loaded from settings). */
  readonly plexMachineId = this._plexMachineId.asReadonly();
  /** Whether Bot-kun AI is explicitly enabled (BotKun:Enabled = true). */
  readonly botkunEnabled = this._botkunEnabled.asReadonly();
  /** Whether Bot-kun AI is configured (BaseUrl + Model both non-empty). */
  readonly botkunConfigured = computed(
    () => this._botkunBaseUrl().trim().length > 0 && this._botkunModel().trim().length > 0,
  );

  constructor() {
    this.refresh();
  }

  /** Reload provider states from the settings API. */
  refresh(): void {
    this.settingsService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (settings) => {
          this._plexEnabled.set(
            settings.find((s) => s.key === 'Plex:Enabled')?.value === 'true',
          );
          this._sonarrEnabled.set(
            settings.find((s) => s.key === 'Sonarr:Enabled')?.value === 'true',
          );
          this._plexMachineId.set(
            settings.find((s) => s.key === 'Plex:MachineIdentifier')?.value ?? '',
          );
          this._botkunEnabled.set(
            settings.find((s) => s.key === 'BotKun:Enabled')?.value === 'true',
          );
          this._botkunBaseUrl.set(
            settings.find((s) => s.key === 'BotKun:BaseUrl')?.value ?? '',
          );
          this._botkunModel.set(
            settings.find((s) => s.key === 'BotKun:Model')?.value ?? '',
          );
        },
        error: () => {
          /* non-critical — keep defaults */
        },
      });
  }

  /** Directly update the Plex enabled state (called by settings after persisting). */
  setPlexEnabled(value: boolean): void {
    this._plexEnabled.set(value);
  }

  /** Directly update the Sonarr enabled state (called by settings after persisting). */
  setSonarrEnabled(value: boolean): void {
    this._sonarrEnabled.set(value);
  }

  /** Directly update the Bot-kun enabled state (called by settings after persisting). */
  setBotKunEnabled(value: boolean): void {
    this._botkunEnabled.set(value);
  }

  /** Update Bot-kun configuration signals (called by settings after persisting). */
  setBotKunConfig(baseUrl: string, model: string): void {
    this._botkunBaseUrl.set(baseUrl);
    this._botkunModel.set(model);
  }
}

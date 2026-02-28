import { Injectable, inject, signal } from '@angular/core';
import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SettingsService } from './settings.service';

/**
 * Centralized service that exposes the enabled/disabled state of optional
 * providers (Plex, Sonarr) as reactive signals. All components inject this
 * service instead of each loading settings independently.
 *
 * Default is `true` (enabled) so the UI renders normally before settings load.
 */
@Injectable({ providedIn: 'root' })
export class ProviderStateService {
  private settingsService = inject(SettingsService);
  private destroyRef = inject(DestroyRef);

  private readonly _plexEnabled = signal<boolean>(true);
  private readonly _sonarrEnabled = signal<boolean>(true);

  /** Whether the Plex provider is enabled (default: true). */
  readonly plexEnabled = this._plexEnabled.asReadonly();
  /** Whether the Sonarr provider is enabled (default: true). */
  readonly sonarrEnabled = this._sonarrEnabled.asReadonly();

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
}

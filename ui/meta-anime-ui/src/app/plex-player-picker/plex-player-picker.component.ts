import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { PopupService } from '../services/popup.service';
import { CastService } from '../services/cast.service';
import { CastDevice } from '../models/anime.models';

/**
 * Modal that lists cast-capable devices and casts the selected show's next episode to the chosen
 * device via Google Cast. Rendered from the app shell, driven by `PopupService`.
 */
@Component({
  selector: 'app-plex-player-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe],
  templateUrl: './plex-player-picker.component.html',
  styleUrl: './plex-player-picker.component.scss',
})
export class PlexPlayerPickerComponent implements OnInit {
  protected popup = inject(PopupService);
  private cast = inject(CastService);

  protected ctx = computed(() => this.popup.plexPlayerPicker());
  protected leaving = computed(() => this.popup.plexPlayerPickerLeaving());

  protected devices = signal<CastDevice[]>([]);
  protected loading = signal(false);
  protected loadError = signal(false);
  protected playing = signal<string | null>(null);
  protected playError = signal<string | null>(null);
  /** Name of the player we successfully launched on; non-null shows the success state. */
  protected launchedOn = signal<string | null>(null);

  ngOnInit(): void {
    this.loadDevices();
  }

  protected loadDevices(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.cast.getDevices().subscribe({
      next: (devices) => {
        this.devices.set(devices ?? []);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadError.set(true);
      },
    });
  }

  protected play(device: CastDevice): void {
    const ctx = this.ctx();
    if (!ctx?.ratingKey || this.playing()) return;

    this.playing.set(device.id);
    this.playError.set(null);
    this.cast.play(device.host, ctx.ratingKey, 0).subscribe({
      next: () => {
        this.playing.set(null);
        this.launchedOn.set(device.name);
      },
      error: (err) => {
        this.playing.set(null);
        this.playError.set(this.extractError(err));
      },
    });
  }

  /** Pulls the backend's error message out of an HttpErrorResponse body. */
  private extractError(err: unknown): string {
    const e = err as { error?: unknown; message?: string };
    const body = e?.error;
    if (typeof body === 'string' && body.trim()) return body;
    if (body && typeof (body as { message?: unknown }).message === 'string')
      return (body as { message: string }).message;
    return e?.message || '';
  }

  protected dismiss(): void {
    this.popup.dismissPlexPlayerPicker();
  }
}

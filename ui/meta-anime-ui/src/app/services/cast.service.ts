import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CastDevice, CastPlayResponse } from '../models/anime.models';

/**
 * Talks to the backend Google Cast endpoints: discovers cast-capable devices on the network and
 * casts a show's next episode to a selected device.
 */
@Injectable({ providedIn: 'root' })
export class CastService {
  private http = inject(HttpClient);
  private base = '/api/v1/cast';

  /** Discovers cast-capable devices (Chromecast / Google TV / Android TV) on the local network. */
  getDevices(): Observable<CastDevice[]> {
    return this.http.get<CastDevice[]>(`${this.base}/devices`);
  }

  /**
   * Casts the next episode of `showRatingKey` to the device at `deviceHost`, resuming at
   * `offsetMs` (0 = from the beginning).
   */
  play(deviceHost: string, showRatingKey: string, offsetMs = 0): Observable<CastPlayResponse> {
    return this.http.post<CastPlayResponse>(`${this.base}/play`, {
      deviceHost,
      showRatingKey,
      offsetMs,
    });
  }
}

import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { timer } from 'rxjs';
import { switchMap, catchError, map } from 'rxjs/operators';
import { of } from 'rxjs';
import { SystemHealthSnapshot } from '../models/health.models';

@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/system';

  /** Polls every 60 s; null until the first successful response or on error. */
  readonly snapshot = toSignal(
    timer(0, 60_000).pipe(
      switchMap(() =>
        this.http.get<SystemHealthSnapshot>(`${this.base}/health`).pipe(
          catchError(() => of(null)),
        ),
      ),
    ),
    { initialValue: null },
  );
}

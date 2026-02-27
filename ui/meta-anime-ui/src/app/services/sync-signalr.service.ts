import { Injectable, OnDestroy, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Observable, Subject } from 'rxjs';
import { SyncStep, SyncStepState, SyncProgressMessage } from '../models/sync-progress.models';
import { ApiConstants } from '../constants/api.constants';
import { RuntimeConfigService } from './runtime-config.service';

/** Ordered step keys matching the backend emission sequence */
const STEP_KEYS = ['kometa', 'plex', 'mal', 'sonarr', 'matching'] as const;

function makeInitialSteps(): SyncStep[] {
  return STEP_KEYS.map((key) => ({
    stepKey: key,
    state: 'pending' as SyncStepState,
    elapsedMs: 0,
  }));
}

@Injectable({ providedIn: 'root' })
export class SyncSignalRService implements OnDestroy {
  private connection: signalR.HubConnection | null = null;
  private readonly runtimeConfig = inject(RuntimeConfigService);

  private readonly _steps$ = new BehaviorSubject<SyncStep[]>(makeInitialSteps());
  private readonly _connected$ = new BehaviorSubject<boolean>(false);
  private readonly _syncDone$ = new BehaviorSubject<boolean>(false);
  private readonly _syncError$ = new BehaviorSubject<boolean>(false);
  private readonly _overallElapsedMs$ = new BehaviorSubject<number>(0);
  /** Emits void once each time a sync run fully completes. No replay — safe to subscribe on mount. */
  private readonly _syncCompleted$ = new Subject<void>();

  /** Step keys that are disabled (excluded from the done check) */
  private _disabledStepKeys = new Set<string>();

  readonly steps$: Observable<SyncStep[]> = this._steps$.asObservable();
  readonly connected$: Observable<boolean> = this._connected$.asObservable();
  readonly syncDone$: Observable<boolean> = this._syncDone$.asObservable();
  readonly syncError$: Observable<boolean> = this._syncError$.asObservable();
  readonly overallElapsedMs$: Observable<number> = this._overallElapsedMs$.asObservable();
  readonly syncCompleted$: Observable<void> = this._syncCompleted$.asObservable();

  /** Elapsed-time ticker: interval handle + last-tick timestamp for delta calculation */
  private tickerHandle: ReturnType<typeof setInterval> | null = null;
  private lastTickAt = 0;
  /** Wall-clock timestamp when the first 'Running' message was received */
  private overallStartedAt: number | null = null;

  connect(): void {
    if (this.connection) return;

    const apiKey = this.runtimeConfig.apiKey;
    const builder = new signalR.HubConnectionBuilder()
      .withUrl(ApiConstants.syncHubUrl, {
        accessTokenFactory: () => apiKey ?? '',
        headers: apiKey ? { [ApiConstants.apiKeyHeader]: apiKey } : {},
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning);

    this.connection = builder.build();

    this.connection.on('SyncProgress', (msg: SyncProgressMessage) => {
      this.handleMessage(msg);
    });

    this.connection.onreconnecting(() => this._connected$.next(false));
    this.connection.onreconnected(() => this._connected$.next(true));
    this.connection.onclose(() => {
      this._connected$.next(false);
      this.stopTicker();
    });

    this.connection
      .start()
      .then(() => this._connected$.next(true))
      .catch(() => this._connected$.next(false));
  }

  disconnect(): void {
    this.stopTicker();
    if (this.connection) {
      this.connection.stop();
      this.connection = null;
    }
    this._connected$.next(false);
  }

  /** Set the step keys for disabled providers (they are excluded from the done/active checks) */
  setDisabledStepKeys(keys: Set<string>): void {
    this._disabledStepKeys = keys;
  }

  /** Reset steps to pending for a new sync run */
  resetSteps(): void {
    this._steps$.next(makeInitialSteps());
    this._syncDone$.next(false);
    this._syncError$.next(false);
    this._overallElapsedMs$.next(0);
    this.overallStartedAt = null;
  }

  /** Called when the HTTP trigger itself fails (before any SignalR progress arrives). */
  markError(): void {
    this._syncError$.next(true);
    this.stopTicker();
  }

  ngOnDestroy(): void {
    this.disconnect();
  }

  private handleMessage(msg: SyncProgressMessage): void {
    const state = this.mapState(msg.state);

    // Track overall start on first Running message
    if (state === 'running' && this.overallStartedAt === null) {
      this.overallStartedAt = Date.now();
    }

    this._steps$.next(
      this._steps$.value.map((s) => {
        if (s.stepKey !== msg.stepKey) return s;
        return {
          ...s,
          state,
          progressPercent: msg.progressPercent,
          // Use elapsed from server message (accurate per-step time from Stopwatch)
          elapsedMs: msg.elapsedMs,
          errorMessage: msg.errorMessage,
        };
      }),
    );

    const allDone = this._steps$.value
      .filter((s) => !this._disabledStepKeys.has(s.stepKey))
      .every((s) => s.state === 'done');
    const anyError = this._steps$.value.some((s) => s.state === 'error');

    if (allDone) {
      this._syncDone$.next(true);
      this._syncCompleted$.next();
      this.stopTicker();
    } else if (anyError) {
      this._syncError$.next(true);
      this.stopTicker();
    } else {
      this.startTicker();
    }
  }

  private mapState(raw: SyncProgressMessage['state']): SyncStepState {
    switch (raw) {
      case 'Running':
        return 'running';
      case 'Done':
        return 'done';
      case 'Error':
        return 'error';
    }
  }

  private startTicker(): void {
    if (this.tickerHandle !== null) return;
    this.lastTickAt = Date.now();
    this.tickerHandle = setInterval(() => {
      const now = Date.now();
      const delta = now - this.lastTickAt;
      this.lastTickAt = now;

      // Increment per-step elapsed for running steps
      this._steps$.next(
        this._steps$.value.map((s) =>
          s.state === 'running' ? { ...s, elapsedMs: s.elapsedMs + delta } : s,
        ),
      );

      // Increment overall elapsed
      if (this.overallStartedAt !== null) {
        this._overallElapsedMs$.next(now - this.overallStartedAt);
      }
    }, 250);
  }

  private stopTicker(): void {
    if (this.tickerHandle !== null) {
      clearInterval(this.tickerHandle);
      this.tickerHandle = null;
    }
  }
}

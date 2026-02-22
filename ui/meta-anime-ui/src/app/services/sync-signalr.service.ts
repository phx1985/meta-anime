import { Injectable, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { SyncStep, SyncStepState, SyncProgressMessage } from '../models/sync-progress.models';

/** Ordered step keys matching the backend emission sequence */
const STEP_KEYS = ['kometa', 'plex', 'mal', 'sonarr', 'matching'] as const;

function makeInitialSteps(): SyncStep[] {
  return STEP_KEYS.map(key => ({
    stepKey: key,
    state: 'pending' as SyncStepState,
    elapsedMs: 0,
  }));
}

@Injectable({ providedIn: 'root' })
export class SyncSignalRService implements OnDestroy {
  private connection: signalR.HubConnection | null = null;

  private readonly _steps$   = new BehaviorSubject<SyncStep[]>(makeInitialSteps());
  private readonly _connected$ = new BehaviorSubject<boolean>(false);
  private readonly _syncDone$  = new BehaviorSubject<boolean>(false);
  private readonly _syncError$ = new BehaviorSubject<boolean>(false);

  readonly steps$:     Observable<SyncStep[]> = this._steps$.asObservable();
  readonly connected$: Observable<boolean>    = this._connected$.asObservable();
  readonly syncDone$:  Observable<boolean>    = this._syncDone$.asObservable();
  readonly syncError$: Observable<boolean>    = this._syncError$.asObservable();

  /** Elapsed-time ticker interval handle */
  private tickerHandle: ReturnType<typeof setInterval> | null = null;

  connect(): void {
    if (this.connection) return;

    const hubUrl = '/hubs/sync';
    const builder = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        // Pass API key as query string for WebSocket upgrade handshake
        accessTokenFactory: () => environment.apiKey ?? '',
        headers: environment.apiKey ? { 'X-Api-Key': environment.apiKey } : {},
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

    this.connection.start()
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

  /** Reset steps to pending for a new sync run */
  resetSteps(): void {
    this._steps$.next(makeInitialSteps());
    this._syncDone$.next(false);
    this._syncError$.next(false);
  }

  ngOnDestroy(): void {
    this.disconnect();
  }

  private handleMessage(msg: SyncProgressMessage): void {
    const state = this.mapState(msg.state);

    this._steps$.next(
      this._steps$.value.map(s => {
        if (s.stepKey !== msg.stepKey) return s;
        return {
          ...s,
          state,
          progressPercent: msg.progressPercent,
          elapsedMs: msg.elapsedMs,
          errorMessage: msg.errorMessage,
          runStartedAt: state === 'running' ? Date.now() : s.runStartedAt,
        };
      })
    );

    const allDone  = this._steps$.value.every(s => s.state === 'done');
    const anyError = this._steps$.value.some(s => s.state === 'error');

    if (allDone) {
      this._syncDone$.next(true);
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
      case 'Running': return 'running';
      case 'Done':    return 'done';
      case 'Error':   return 'error';
    }
  }

  private startTicker(): void {
    if (this.tickerHandle !== null) return;
    this.tickerHandle = setInterval(() => {
      const now = Date.now();
      this._steps$.next(
        this._steps$.value.map(s =>
          s.state === 'running' && s.runStartedAt
            ? { ...s, elapsedMs: s.elapsedMs + (now - (s.runStartedAt ?? now)) }
            : s
        )
      );
    }, 250);
  }

  private stopTicker(): void {
    if (this.tickerHandle !== null) {
      clearInterval(this.tickerHandle);
      this.tickerHandle = null;
    }
  }
}

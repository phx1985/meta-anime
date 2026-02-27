// State of a single sync step as seen by the frontend
export type SyncStepState = 'pending' | 'running' | 'done' | 'error';

export interface SyncStep {
  /** i18n key fragment, e.g. "kometa", "plex", "mal", "sonarr", "matching" */
  stepKey: string;
  state: SyncStepState;
  /** 0–100, optional */
  progressPercent?: number;
  /** milliseconds elapsed for this step (server timestamp when done; ticker-incremented while running) */
  elapsedMs: number;
  errorMessage?: string;
}

/** Raw message received from the SignalR hub */
export interface SyncProgressMessage {
  stepKey: string;
  /** "Running" | "Done" | "Error" — matches C# enum names */
  state: 'Running' | 'Done' | 'Error';
  progressPercent?: number;
  elapsedMs: number;
  errorMessage?: string;
}

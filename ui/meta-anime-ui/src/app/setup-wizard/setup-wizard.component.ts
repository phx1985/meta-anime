import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  OnDestroy,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { trigger, transition, style, animate } from '@angular/animations';
import { EnrollmentService } from '../services/enrollment.service';
import { SettingsService } from '../services/settings.service';
import { AnimeService } from '../services/anime.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { NotificationService } from '../services/notification.service';
import { HelpModalComponent } from '../help-modal/help-modal.component';
import { SyncStep } from '../models/sync-progress.models';
import { fmtElapsed } from '../utils/format.utils';
import { filter } from 'rxjs/operators';

const WIZARD_ACTIVE_KEY = 'wizard_active';
const PLEX_POLL_INTERVAL_MS = 3000;
const PLEX_POLL_MAX = 40;

@Component({
  selector: 'app-setup-wizard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, ReactiveFormsModule, HelpModalComponent],
  templateUrl: './setup-wizard.component.html',
  styleUrl: './setup-wizard.component.scss',
  animations: [
    trigger('slideStep', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(40px)' }),
        animate('320ms cubic-bezier(0.2, 0, 0.2, 1)', style({ opacity: 1, transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        style({ opacity: 1, transform: 'translateX(0)' }),
        animate('220ms cubic-bezier(0.4, 0, 1, 1)', style({ opacity: 0, transform: 'translateX(-40px)' })),
      ]),
    ]),
  ],
})
export class SetupWizardComponent implements OnInit, OnDestroy {
  private enrollmentService = inject(EnrollmentService);
  private settingsService = inject(SettingsService);
  private animeService = inject(AnimeService);
  readonly signalR = inject(SyncSignalRService);
  private notifService = inject(NotificationService);
  private fb = inject(FormBuilder);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  readonly fmtElapsed = fmtElapsed;
  readonly TOTAL_STEPS = 6;
  /** The exact OAuth callback URL that the user must register in MAL developer portal */
  readonly malCallbackUrl = window.location.origin + '/auth/mal/callback';

  step = signal(1);

  // ── Loading state ──────────────────────────────────────────────────────────
  initializing = signal(true);

  // ── MAL state ─────────────────────────────────────────────────────────────
  malConnected = signal(false);
  malConnecting = signal(false);
  malClientIdControl = new FormControl('', [Validators.required]);

  // ── Plex state ────────────────────────────────────────────────────────────
  plexConnected = signal(false);
  plexConnecting = signal(false);
  private plexPollIntervalId: ReturnType<typeof setInterval> | null = null;

  // ── Sonarr state ──────────────────────────────────────────────────────────
  sonarrForm: FormGroup = this.fb.group({
    host: ['', Validators.required],
    apiKey: ['', Validators.required],
  });
  sonarrSaving = signal(false);
  sonarrSaved = signal(false);
  sonarrError = signal('');

  // ── Sync state ────────────────────────────────────────────────────────────
  syncing = signal(false);
  syncHttpError = signal<string | null>(null);
  syncSteps = toSignal(this.signalR.steps$, { initialValue: [] as SyncStep[] });
  syncDone = toSignal(this.signalR.syncDone$, { initialValue: false });
  syncError = toSignal(this.signalR.syncError$, { initialValue: false });

  // ── Help modals ───────────────────────────────────────────────────────────
  malHelpOpen = signal(false);
  sonarrHelpOpen = signal(false);

  ngOnInit(): void {
    this.signalR.connect();
    this.loadInitialState();

    // Listen for sync completion to update the syncing flag
    this.signalR.syncDone$
      .pipe(
        filter((v) => v),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        this.syncing.set(false);
      });

    this.signalR.syncError$
      .pipe(
        filter((v) => v),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        this.syncing.set(false);
      });
  }

  private loadInitialState(): void {
    const wizardActive = localStorage.getItem(WIZARD_ACTIVE_KEY) === 'true';

    // Pre-populate Client ID from settings if already set (non-critical; failure is silent)
    this.settingsService.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (settings) => {
          const clientId = settings.find((s) => s.key === 'MAL:ClientId')?.value ?? '';
          if (clientId) this.malClientIdControl.setValue(clientId);
        },
        error: (_err) => { /* Non-critical pre-population; wizard still works without it */ },
      });

    this.enrollmentService
      .getMalStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          this.malConnected.set(s.connected);
          if (s.connected && wizardActive) {
            // Returning from MAL OAuth redirect — resume at Plex step
            this.step.set(3);
            this.loadPlexStatus();
          }
          this.initializing.set(false);
        },
        error: () => {
          this.initializing.set(false);
        },
      });
  }

  private loadPlexStatus(): void {
    this.enrollmentService
      .getPlexStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => this.plexConnected.set(s.connected),
        error: () => {},
      });
  }

  // ── Navigation ─────────────────────────────────────────────────────────────

  goToStep(n: number): void {
    this.step.set(n);
  }

  nextStep(): void {
    this.step.update((s) => s + 1);
  }

  prevStep(): void {
    this.step.update((s) => Math.max(1, s - 1));
  }

  // ── MAL ───────────────────────────────────────────────────────────────────

  connectMal(): void {
    const clientId = (this.malClientIdControl.value ?? '').trim();
    if (!clientId) return;

    this.malConnecting.set(true);
    localStorage.setItem(WIZARD_ACTIVE_KEY, 'true');

    // Save the Client ID to settings first, then initiate OAuth
    this.settingsService
      .update('MAL:ClientId', { value: clientId })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.enrollmentService
            .initiateMalAuth()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              next: (res) => {
                this.malConnecting.set(false);
                window.location.href = res.authorizationUrl;
              },
              error: () => {
                this.malConnecting.set(false);
                localStorage.removeItem(WIZARD_ACTIVE_KEY);
              },
            });
        },
        error: () => {
          this.malConnecting.set(false);
          localStorage.removeItem(WIZARD_ACTIVE_KEY);
        },
      });
  }

  // ── Plex ──────────────────────────────────────────────────────────────────

  connectPlex(): void {
    this.plexConnecting.set(true);
    this.enrollmentService
      .initiatePlexPin()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          window.open(res.authUrl, '_blank');
          this.startPlexPolling(res.pinId);
        },
        error: () => {
          this.plexConnecting.set(false);
        },
      });
  }

  private stopPlexPolling(): void {
    if (this.plexPollIntervalId) {
      clearInterval(this.plexPollIntervalId);
      this.plexPollIntervalId = null;
    }
  }

  private startPlexPolling(pinId: number): void {
    let pollCount = 0;
    this.stopPlexPolling();
    this.plexPollIntervalId = setInterval(() => {
      pollCount++;
      this.enrollmentService
        .pollPlexPin(pinId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (res) => {
            if (res.connected) {
              this.stopPlexPolling();
              this.plexConnecting.set(false);
              this.plexConnected.set(true);
            } else if (pollCount >= PLEX_POLL_MAX) {
              this.stopPlexPolling();
              this.plexConnecting.set(false);
            }
          },
          error: () => {
            this.stopPlexPolling();
            this.plexConnecting.set(false);
          },
        });
    }, PLEX_POLL_INTERVAL_MS);
  }

  // ── Sonarr ────────────────────────────────────────────────────────────────

  saveSonarrAndContinue(): void {
    if (this.sonarrForm.invalid) return;
    const { host, apiKey } = this.sonarrForm.value as { host: string; apiKey: string };
    this.sonarrSaving.set(true);
    this.sonarrError.set('');

    const saveHost = this.settingsService.update('Sonarr:Host', { value: host });
    const saveKey = this.settingsService.update('Sonarr:ApiKey', { value: apiKey });

    let done = 0;
    const finish = () => {
      done++;
      if (done === 2) {
        this.sonarrSaving.set(false);
        this.sonarrSaved.set(true);
        this.nextStep();
      }
    };
    const onErr = (err: unknown) => {
      this.sonarrSaving.set(false);
      this.sonarrError.set(
        err instanceof HttpErrorResponse ? (err.error?.message ?? err.message) : 'Failed to save',
      );
    };

    saveHost.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: finish, error: onErr });
    saveKey.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: finish, error: onErr });
  }

  // ── Sync ──────────────────────────────────────────────────────────────────

  runSync(): void {
    if (this.syncing()) return;
    this.syncing.set(true);
    this.syncHttpError.set(null);
    this.signalR.resetSteps();

    this.animeService
      .syncAnimes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => { /* SignalR handles completion */ },
        error: (err: unknown) => {
          this.syncing.set(false);
          const message =
            err instanceof HttpErrorResponse
              ? (err.error?.message ?? err.message ?? 'Sync failed')
              : 'Sync failed';
          this.syncHttpError.set(message);
          this.signalR.markError();
        },
      });
  }

  // ── Completion ────────────────────────────────────────────────────────────

  finish(): void {
    localStorage.removeItem(WIZARD_ACTIVE_KEY);
    this.router.navigate(['/']);
  }

  ngOnDestroy(): void {
    this.stopPlexPolling();
  }
}

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
import { trigger, transition, style, animate, query } from '@angular/animations';
import { EnrollmentService, PlexLibrary } from '../services/enrollment.service';
import { SettingsService } from '../services/settings.service';
import { AnimeService } from '../services/anime.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { NotificationService } from '../services/notification.service';
import { SonarrService, SonarrTag } from '../services/sonarr.service';
import { HelpModalComponent } from '../help-modal/help-modal.component';
import { SyncStep } from '../models/sync-progress.models';
import { fmtElapsed } from '../utils/format.utils';
import { filter } from 'rxjs/operators';
import { forkJoin } from 'rxjs';

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
      transition('* => *', [
        query(':leave', [
          style({ position: 'absolute', top: 0, left: 0, width: '100%',
                  opacity: 1, transform: 'translateX(0)' }),
          animate('200ms cubic-bezier(0.4, 0, 1, 1)',
                  style({ opacity: 0, transform: 'translateX(-48px)' })),
        ], { optional: true }),
        query(':enter', [
          style({ position: 'absolute', top: 0, left: 0, width: '100%',
                  opacity: 0, transform: 'translateX(48px)' }),
          animate('280ms 80ms cubic-bezier(0.2, 0, 0.2, 1)',
                  style({ opacity: 1, transform: 'translateX(0)' })),
        ], { optional: true }),
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
  private sonarrService = inject(SonarrService);
  private fb = inject(FormBuilder);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);

  readonly fmtElapsed = fmtElapsed;
  readonly TOTAL_STEPS = 6;
  /** The exact OAuth callback URL that the user must register in MAL developer portal */
  readonly malCallbackUrl = window.location.origin + '/auth/mal/callback';

  step = signal(1);
  animStep = signal(1);

  // ── Loading state ──────────────────────────────────────────────────────────
  initializing = signal(true);

  // ── MAL state ─────────────────────────────────────────────────────────────
  malConnected = signal(false);
  malConnecting = signal(false);
  malClientIdControl = new FormControl('', [Validators.required]);

  // ── Plex state ────────────────────────────────────────────────────────────
  plexConnected = signal(false);
  plexConnecting = signal(false);
  plexServerUrlControl = new FormControl('', []);
  plexLibraries = signal<PlexLibrary[]>([]);
  plexLibrariesLoading = signal(false);
  plexSelectedLibKeys = signal<string[]>([]);
  private plexPollIntervalId: ReturnType<typeof setInterval> | null = null;

  // ── Sonarr state ──────────────────────────────────────────────────────────
  sonarrForm: FormGroup = this.fb.group({
    localUrl: ['', Validators.required],
    remoteUrl: [''],
    apiKey: ['', Validators.required],
  });
  sonarrSaving = signal(false);
  sonarrSaved = signal(false);
  sonarrError = signal('');
  sonarrTags = signal<SonarrTag[]>([]);
  sonarrTagsLoading = signal(false);
  sonarrTagsLoaded = signal(false);
  sonarrTagsError = signal('');
  sonarrSelectedTagIds = signal<number[]>([]);

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

    // Pre-populate settings if already set (non-critical; failure is silent)
    this.settingsService.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (settings) => {
          const clientId = settings.find((s) => s.key === 'MAL:ClientId')?.value ?? '';
          if (clientId) this.malClientIdControl.setValue(clientId);
          const plexHost = settings.find((s) => s.key === 'Plex:Host')?.value ?? '';
          if (plexHost) this.plexServerUrlControl.setValue(plexHost);
          const sonarrHost = settings.find((s) => s.key === 'Sonarr:Host')?.value ?? '';
          if (sonarrHost) this.sonarrForm.patchValue({ localUrl: sonarrHost });
          const sonarrRemote = settings.find((s) => s.key === 'Sonarr:RemoteUrl')?.value ?? '';
          if (sonarrRemote) this.sonarrForm.patchValue({ remoteUrl: sonarrRemote });
          const sonarrApiKey = settings.find((s) => s.key === 'Sonarr:ApiKey')?.value ?? '';
          if (sonarrApiKey) this.sonarrForm.patchValue({ apiKey: sonarrApiKey });
        },
        error: (_err) => { /* Non-critical */ },
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
            this.animStep.set(3);
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
        next: (s) => {
          this.plexConnected.set(s.connected);
          if (s.connected) {
            this.loadPlexLibraries();
          }
        },
        error: () => {},
      });
  }

  private loadPlexLibraries(): void {
    this.plexLibrariesLoading.set(true);
    this.enrollmentService
      .getPlexLibraries()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (libs) => {
          this.plexLibraries.set(libs);
          this.plexLibrariesLoading.set(false);
        },
        error: () => {
          this.plexLibrariesLoading.set(false);
        },
      });
  }

  // ── Navigation ─────────────────────────────────────────────────────────────

  goToStep(n: number): void {
    this.step.set(n);
    this.animStep.set(n);
  }

  nextStep(): void {
    const next = this.step() + 1;
    this.step.set(next);
    this.animStep.set(next);
  }

  prevStep(): void {
    const prev = Math.max(1, this.step() - 1);
    this.step.set(prev);
    this.animStep.set(prev);
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

  savePlexUrlAndConnect(): void {
    const url = (this.plexServerUrlControl.value ?? '').trim();
    if (!url) {
      this.connectPlex();
      return;
    }
    this.settingsService.update('Plex:Host', { value: url })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => this.connectPlex(), error: () => this.connectPlex() });
  }

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

  togglePlexLibrary(key: string): void {
    const current = this.plexSelectedLibKeys();
    if (current.includes(key)) {
      this.plexSelectedLibKeys.set(current.filter((k) => k !== key));
    } else {
      this.plexSelectedLibKeys.set([...current, key]);
    }
  }

  isPlexLibSelected(key: string): boolean {
    return this.plexSelectedLibKeys().includes(key);
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
              this.loadPlexLibraries();
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

  testSonarrAndLoadTags(): void {
    if (this.sonarrForm.get('localUrl')?.invalid || this.sonarrForm.get('apiKey')?.invalid) return;
    const { localUrl, apiKey } = this.sonarrForm.value as { localUrl: string; apiKey: string };

    this.sonarrTagsLoading.set(true);
    this.sonarrTagsError.set('');

    // Save credentials first so backend uses them for the tags call
    forkJoin([
      this.settingsService.update('Sonarr:Host', { value: localUrl }),
      this.settingsService.update('Sonarr:ApiKey', { value: apiKey }),
    ])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.sonarrService
            .getTags()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              next: (tags) => {
                this.sonarrTags.set(tags);
                this.sonarrTagsLoaded.set(true);
                this.sonarrTagsLoading.set(false);
              },
              error: (err: unknown) => {
                this.sonarrTagsLoading.set(false);
                this.sonarrTagsError.set(
                  err instanceof HttpErrorResponse
                    ? (err.error?.message ?? err.message ?? 'Failed to load tags')
                    : 'Failed to connect to Sonarr',
                );
              },
            });
        },
        error: (err: unknown) => {
          this.sonarrTagsLoading.set(false);
          this.sonarrTagsError.set(
            err instanceof HttpErrorResponse ? (err.error?.message ?? err.message) : 'Failed to save',
          );
        },
      });
  }

  toggleSonarrTag(id: number): void {
    const current = this.sonarrSelectedTagIds();
    if (current.includes(id)) {
      this.sonarrSelectedTagIds.set(current.filter((t) => t !== id));
    } else {
      this.sonarrSelectedTagIds.set([...current, id]);
    }
  }

  isSonarrTagSelected(id: number): boolean {
    return this.sonarrSelectedTagIds().includes(id);
  }

  saveSonarrAndContinue(): void {
    if (this.sonarrForm.invalid) return;
    const { localUrl, remoteUrl, apiKey } = this.sonarrForm.value as {
      localUrl: string; remoteUrl: string; apiKey: string;
    };
    this.sonarrSaving.set(true);
    this.sonarrError.set('');

    // Build selected tag labels (save as comma-separated tag names)
    const selectedTagLabels = this.sonarrSelectedTagIds()
      .map((id) => this.sonarrTags().find((t) => t.id === id)?.label ?? '')
      .filter(Boolean)
      .join(',');

    const saves = [
      this.settingsService.update('Sonarr:Host', { value: localUrl }),
      this.settingsService.update('Sonarr:ApiKey', { value: apiKey }),
      this.settingsService.update('Sonarr:Enabled', { value: 'true' }),
    ];
    if (remoteUrl?.trim()) {
      saves.push(this.settingsService.update('Sonarr:RemoteUrl', { value: remoteUrl.trim() }));
    }
    if (selectedTagLabels) {
      saves.push(this.settingsService.update('Sonarr:AnimeTags', { value: selectedTagLabels }));
    }

    forkJoin(saves)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.sonarrSaving.set(false);
          this.sonarrSaved.set(true);
          this.nextStep();
        },
        error: (err: unknown) => {
          this.sonarrSaving.set(false);
          this.sonarrError.set(
            err instanceof HttpErrorResponse ? (err.error?.message ?? err.message) : 'Failed to save',
          );
        },
      });
  }

  savePlexLibrariesAndNext(): void {
    const keys = this.plexSelectedLibKeys();
    if (keys.length > 0) {
      this.settingsService
        .update('Plex:AnimeSections', { value: keys.join(',') })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: () => this.settingsService
            .update('Plex:Enabled', { value: 'true' })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({ next: () => this.nextStep(), error: () => this.nextStep() }),
          error: () => this.nextStep(),
        });
    } else {
      this.nextStep();
    }
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

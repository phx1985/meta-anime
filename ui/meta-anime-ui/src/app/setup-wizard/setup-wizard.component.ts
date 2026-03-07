import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  OnDestroy,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import confetti from 'canvas-confetti';
import { EnrollmentService, PlexLibrary } from '../services/enrollment.service';
import { SettingsService } from '../services/settings.service';
import { AnimeService } from '../services/anime.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { NotificationService } from '../services/notification.service';
import { SonarrService, SonarrTag } from '../services/sonarr.service';
import { HelpModalComponent } from '../help-modal/help-modal.component';
import { ProviderStateService } from '../services/provider-state.service';
import { AuthService } from '../services/auth.service';
import { ThemeService } from '../services/theme.service';
import { StorageKeys } from '../constants/storage-keys';
import { SyncStep } from '../models/sync-progress.models';
import { fmtElapsed } from '../utils/format.utils';
import { filter } from 'rxjs/operators';
import { forkJoin } from 'rxjs';

const PLEX_POLL_INTERVAL_MS = 3000;
const PLEX_POLL_MAX = 40;

@Component({
  selector: 'app-setup-wizard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, ReactiveFormsModule, HelpModalComponent],
  templateUrl: './setup-wizard.component.html',
  styleUrl: './setup-wizard.component.scss',
})
export class SetupWizardComponent implements OnInit, OnDestroy {
  private enrollmentService = inject(EnrollmentService);
  private settingsService = inject(SettingsService);
  private animeService = inject(AnimeService);
  readonly signalR = inject(SyncSignalRService);
  private notifService = inject(NotificationService);
  private sonarrService = inject(SonarrService);
  private providerState = inject(ProviderStateService);
  private authService = inject(AuthService);
  private translate = inject(TranslateService);
  readonly themeService = inject(ThemeService);
  private fb = inject(FormBuilder);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);

  currentLang = signal<string>(localStorage.getItem(StorageKeys.language) || 'en');

  readonly availableLanguages: ReadonlyArray<{ code: string; label: string }> = [
    { code: 'en', label: 'English' },
    { code: 'fr', label: 'Français' },
  ];

  readonly currentTheme = computed<'light' | 'dark' | 'system'>(() => {
    if (this.themeService.followOsTheme()) return 'system';
    return this.themeService.isDarkTheme() ? 'dark' : 'light';
  });

  switchLanguage(code: string): void {
    this.currentLang.set(code);
    this.translate.use(code);
    localStorage.setItem(StorageKeys.language, code);
  }

  setTheme(theme: 'light' | 'dark' | 'system'): void {
    if (theme === 'system') {
      this.themeService.setFollowOsTheme(true);
    } else {
      this.themeService.setDarkTheme(theme === 'dark');
    }
  }

  readonly fmtElapsed = fmtElapsed;
  readonly TOTAL_STEPS = 7;
  readonly malCallbackUrl = window.location.origin + '/auth/mal/callback';

  step = signal(1);
  animStep = signal(1);

  // Loading state
  initializing = signal(true);

  // Security (step 2)
  securityForm: FormGroup = this.fb.group(
    {
      enableAuth: [false],
      password: [''],
      confirmPassword: [''],
    },
    { validators: this.passwordMatchValidator },
  );
  securitySaving = signal(false);
  securityError = signal('');

  // MAL state
  malConnected = signal(false);
  malConnecting = signal(false);
  malError = signal(false);
  malClientIdControl = new FormControl('', [Validators.required]);
  malClientSecretControl = new FormControl('', [Validators.required]);

  // Plex state
  plexConnected = signal(false);
  plexConnecting = signal(false);
  plexServerUrlControl = new FormControl('', [Validators.required]);
  plexLibraries = signal<PlexLibrary[]>([]);
  plexLibrariesLoading = signal(false);
  plexSelectedLibKeys = signal<string[]>([]);
  private plexPollIntervalId: ReturnType<typeof setInterval> | null = null;

  // Sonarr state
  sonarrForm: FormGroup = this.fb.group({
    localUrl: ['', Validators.required],
    remoteUrl: [''],
    apiKey: ['', Validators.required],
    rootFolder: [''],
    qualityProfile: [''],
    seasonFolder: [true],
    monitor: ['all'],
  });
  sonarrSaving = signal(false);
  sonarrSaved = signal(false);
  sonarrError = signal('');
  sonarrTags = signal<SonarrTag[]>([]);
  sonarrTagsLoading = signal(false);
  sonarrTagsLoaded = signal(false);
  sonarrTagsError = signal('');
  sonarrSelectedTagIds = signal<number[]>([]);
  sonarrTagsTouched = signal(false);
  sonarrRootFolders = signal<{ id: number; path: string }[]>([]);
  sonarrQualityProfiles = signal<{ id: number; name: string }[]>([]);
  sonarrOptionsLoading = signal(false);
  sonarrOptionsError = signal('');
  sonarrNewTagLabel = signal('');
  sonarrCreatingTag = signal(false);

  readonly MONITOR_OPTIONS = ['all', 'future', 'missing', 'existing', 'recent', 'pilot', 'firstSeason', 'lastSeason', 'none'];

  // Sync state
  syncing = signal(false);
  syncHttpError = signal<string | null>(null);
  syncSteps = toSignal(this.signalR.steps$, { initialValue: [] as SyncStep[] });
  syncDone = toSignal(this.signalR.syncDone$, { initialValue: false });
  syncError = toSignal(this.signalR.syncError$, { initialValue: false });

  // Help modals
  malHelpOpen = signal(false);
  sonarrHelpOpen = signal(false);
  malCallbackCopied = signal(false);
  private malCallbackCopyTimer: ReturnType<typeof setTimeout> | null = null;

  copyMalCallbackUrl(): void {
    navigator.clipboard.writeText(this.malCallbackUrl).then(() => {
      this.malCallbackCopied.set(true);
      if (this.malCallbackCopyTimer) clearTimeout(this.malCallbackCopyTimer);
      this.malCallbackCopyTimer = setTimeout(() => this.malCallbackCopied.set(false), 2000);
    });
  }

  constructor() {
    // Set wizard-active flag as early as possible so NotificationService
    // suppresses all toasts before any lifecycle hooks fire.
    this.notifService.setWizardActive(true);
    effect(() => {
      if (this.step() === 7) {
        this.launchFireworks();
      }
    });
  }

  ngOnInit(): void {
    this.signalR.connect();
    this.loadInitialState();

    this.signalR.syncDone$
      .pipe(filter((v) => v), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => { this.syncing.set(false); });

    this.signalR.syncError$
      .pipe(filter((v) => v), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => { this.syncing.set(false); });
  }

  private loadInitialState(): void {
    const malErrorParam = this.route.snapshot.queryParams['mal_error'] === 'true';

    if (malErrorParam) {
      // Clear the mal_error query param from the URL without re-triggering navigation
      void this.router.navigate([], { replaceUrl: true, queryParams: {} });
    }

    this.settingsService.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (settings) => {
          const find = (k: string) => settings.find((s) => s.key === k)?.value ?? '';
          const clientId = find('MAL:ClientId');
          if (clientId) this.malClientIdControl.setValue(clientId);
          const clientSecret = find('MAL:ClientSecret');
          if (clientSecret) this.malClientSecretControl.setValue(clientSecret);
          const plexHost = find('Plex:Host');
          if (plexHost) this.plexServerUrlControl.setValue(plexHost);
          const sonarrHost = find('Sonarr:Host');
          if (sonarrHost) this.sonarrForm.patchValue({ localUrl: sonarrHost });
          const sonarrRemote = find('Sonarr:RemoteUrl');
          if (sonarrRemote) this.sonarrForm.patchValue({ remoteUrl: sonarrRemote });
          const sonarrApiKey = find('Sonarr:ApiKey');
          if (sonarrApiKey) this.sonarrForm.patchValue({ apiKey: sonarrApiKey });
          const rootFolder = find('Sonarr:DefaultRootFolder');
          if (rootFolder) this.sonarrForm.patchValue({ rootFolder });
          const qualityProfile = find('Sonarr:DefaultQualityProfileName');
          if (qualityProfile) this.sonarrForm.patchValue({ qualityProfile });
          const seasonFolderStr = find('Sonarr:DefaultSeasonFolder');
          this.sonarrForm.patchValue({ seasonFolder: seasonFolderStr !== 'false' });
          const monitor = find('Sonarr:DefaultMonitor') || 'all';
          this.sonarrForm.patchValue({ monitor });
        },
        error: () => {},
      });

    this.enrollmentService
      .getMalStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          this.malConnected.set(s.connected);
          if (s.connected && this.notifService.wizardActive()) {
            this.step.set(4);
            this.animStep.set(4);
            this.loadPlexStatus();
          } else if (!s.connected && this.notifService.wizardActive() && malErrorParam) {
            // Returned from a failed MAL OAuth round-trip — stay on step 3 and show inline error
            this.step.set(3);
            this.animStep.set(3);
            this.malError.set(true);
          }
          this.initializing.set(false);
        },
        error: () => { this.initializing.set(false); },
      });
  }

  private loadPlexStatus(): void {
    this.enrollmentService
      .getPlexStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          this.plexConnected.set(s.connected);
          if (s.connected) this.loadPlexLibraries();
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
        next: (libs) => { this.plexLibraries.set(libs); this.plexLibrariesLoading.set(false); },
        error: () => { this.plexLibrariesLoading.set(false); },
      });
  }

  // Navigation
  goToStep(n: number): void { this.step.set(n); this.animStep.set(n); }
  nextStep(): void { const n = this.step() + 1; this.step.set(n); this.animStep.set(n); }
  prevStep(): void { const p = Math.max(1, this.step() - 1); this.step.set(p); this.animStep.set(p); }

  shouldShowStep(stepKey: string): boolean {
    if (stepKey.toLowerCase().includes('plex')) return this.plexConnected();
    if (stepKey.toLowerCase().includes('sonarr')) return this.sonarrSaved();
    return true;
  }

  // Security (step 2)
  private passwordMatchValidator(group: AbstractControl): ValidationErrors | null {
    const pw = group.get('password')?.value as string;
    const cpw = group.get('confirmPassword')?.value as string;
    const enabled = group.get('enableAuth')?.value as boolean;
    if (!enabled) return null;
    return pw === cpw ? null : { passwordMismatch: true };
  }

  saveSecurityAndNext(): void {
    const { enableAuth, password } = this.securityForm.value as { enableAuth: boolean; password: string };
    if (!enableAuth) { this.nextStep(); return; }

    if (this.securityForm.invalid) return;

    this.securitySaving.set(true);
    this.securityError.set('');
    this.authService.setupPassword(password)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => { this.securitySaving.set(false); this.nextStep(); },
        error: (err: unknown) => {
          this.securitySaving.set(false);
          this.securityError.set(err instanceof HttpErrorResponse ? (err.error?.message ?? err.message) : 'Failed to set password');
        },
      });
  }

  // MAL
  connectMal(): void {
    const clientId = (this.malClientIdControl.value ?? '').trim();
    const clientSecret = (this.malClientSecretControl.value ?? '').trim();
    if (!clientId || !clientSecret) return;
    this.malConnecting.set(true);
    this.malError.set(false);
    forkJoin([
      this.settingsService.update('MAL:ClientId', { value: clientId }),
      this.settingsService.update('MAL:ClientSecret', { value: clientSecret }),
    ])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.enrollmentService.initiateMalAuth()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              next: (res) => { window.location.href = res.authorizationUrl; },
              error: () => { this.malConnecting.set(false); },
            });
        },
        error: () => { this.malConnecting.set(false); },
      });
  }

  // Plex
  savePlexUrlAndConnect(): void {
    const url = (this.plexServerUrlControl.value ?? '').trim();
    if (!url) { this.connectPlex(); return; }
    this.settingsService.update('Plex:Host', { value: url })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => this.connectPlex(), error: () => this.connectPlex() });
  }

  connectPlex(): void {
    this.plexConnecting.set(true);
    this.enrollmentService.initiatePlexPin()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => { window.open(res.authUrl, '_blank'); this.startPlexPolling(res.pinId); },
        error: () => { this.plexConnecting.set(false); },
      });
  }

  togglePlexLibrary(key: string): void {
    const c = this.plexSelectedLibKeys();
    this.plexSelectedLibKeys.set(c.includes(key) ? c.filter((k) => k !== key) : [...c, key]);
  }
  isPlexLibSelected(key: string): boolean { return this.plexSelectedLibKeys().includes(key); }

  private stopPlexPolling(): void {
    if (this.plexPollIntervalId) { clearInterval(this.plexPollIntervalId); this.plexPollIntervalId = null; }
  }

  private startPlexPolling(pinId: number): void {
    let pollCount = 0;
    this.stopPlexPolling();
    this.plexPollIntervalId = setInterval(() => {
      pollCount++;
      this.enrollmentService.pollPlexPin(pinId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (res) => {
            if (res.connected) {
              this.stopPlexPolling(); this.plexConnecting.set(false);
              this.plexConnected.set(true); this.loadPlexLibraries();
            } else if (pollCount >= PLEX_POLL_MAX) {
              this.stopPlexPolling(); this.plexConnecting.set(false);
            }
          },
          error: () => { this.stopPlexPolling(); this.plexConnecting.set(false); },
        });
    }, PLEX_POLL_INTERVAL_MS);
  }

  // Sonarr
  testSonarrAndLoadTags(): void {
    if (this.sonarrForm.get('localUrl')?.invalid || this.sonarrForm.get('apiKey')?.invalid) return;
    const { localUrl, apiKey } = this.sonarrForm.value as { localUrl: string; apiKey: string };
    this.sonarrTagsLoading.set(true);
    this.sonarrTagsError.set('');
    this.sonarrOptionsError.set('');
    forkJoin([
      this.settingsService.update('Sonarr:Host', { value: localUrl }),
      this.settingsService.update('Sonarr:ApiKey', { value: apiKey }),
    ])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          forkJoin([this.sonarrService.getTags(), this.sonarrService.getOptions()])
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              next: ([tags, options]) => {
                this.sonarrTags.set(tags);
                this.sonarrTagsLoaded.set(true);
                this.sonarrTagsLoading.set(false);
                this.sonarrRootFolders.set(options.rootFolders ?? []);
                this.sonarrQualityProfiles.set(options.qualityProfiles ?? []);
                const { rootFolder, qualityProfile } = this.sonarrForm.value as { rootFolder: string; qualityProfile: string };
                if (!rootFolder && options.rootFolders?.length)
                  this.sonarrForm.patchValue({ rootFolder: options.rootFolders[0].path });
                if (!qualityProfile && options.qualityProfiles?.length)
                  this.sonarrForm.patchValue({ qualityProfile: options.qualityProfiles[0].name });
              },
              error: (err: unknown) => {
                this.sonarrTagsLoading.set(false);
                this.sonarrTagsError.set(
                  err instanceof HttpErrorResponse
                    ? (err.error?.message ?? err.message ?? 'Failed to load Sonarr data')
                    : 'Failed to connect to Sonarr',
                );
              },
            });
        },
        error: (err: unknown) => {
          this.sonarrTagsLoading.set(false);
          this.sonarrTagsError.set(err instanceof HttpErrorResponse ? (err.error?.message ?? err.message) : 'Failed to save');
        },
      });
  }

  toggleSonarrTag(id: number): void {
    const c = this.sonarrSelectedTagIds();
    this.sonarrSelectedTagIds.set(c.includes(id) ? c.filter((t) => t !== id) : [...c, id]);
    this.sonarrTagsTouched.set(true);
  }
  isSonarrTagSelected(id: number): boolean { return this.sonarrSelectedTagIds().includes(id); }

  createSonarrTag(): void {
    const label = this.sonarrNewTagLabel().trim();
    if (!label) return;
    this.sonarrCreatingTag.set(true);
    this.sonarrService.createTag(label)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (tag) => {
          this.sonarrTags.update((tags) => [...tags, tag]);
          this.sonarrSelectedTagIds.update((ids) => [...ids, tag.id]);
          this.sonarrNewTagLabel.set('');
          this.sonarrCreatingTag.set(false);
        },
        error: () => { this.sonarrCreatingTag.set(false); },
      });
  }

  saveSonarrAndContinue(): void {
    if (this.sonarrForm.invalid) return;
    const { localUrl, remoteUrl, apiKey, rootFolder, qualityProfile, seasonFolder, monitor } = this.sonarrForm.value as {
      localUrl: string; remoteUrl: string; apiKey: string;
      rootFolder: string; qualityProfile: string; seasonFolder: boolean; monitor: string;
    };
    this.sonarrSaving.set(true);
    this.sonarrError.set('');

    const selectedTagLabels = this.sonarrSelectedTagIds()
      .map((id) => this.sonarrTags().find((t) => t.id === id)?.label ?? '')
      .filter(Boolean).join(',');

    const saves = [
      this.settingsService.update('Sonarr:Host', { value: localUrl }),
      this.settingsService.update('Sonarr:ApiKey', { value: apiKey }),
      this.settingsService.update('Sonarr:Enabled', { value: 'true' }),
      this.settingsService.update('Sonarr:DefaultSeasonFolder', { value: String(seasonFolder ?? true) }),
      this.settingsService.update('Sonarr:DefaultMonitor', { value: monitor ?? 'all' }),
    ];
    if (remoteUrl?.trim()) saves.push(this.settingsService.update('Sonarr:RemoteUrl', { value: remoteUrl.trim() }));
    if (selectedTagLabels) saves.push(this.settingsService.update('Sonarr:AnimeTags', { value: selectedTagLabels }));
    if (rootFolder?.trim()) saves.push(this.settingsService.update('Sonarr:DefaultRootFolder', { value: rootFolder.trim() }));
    if (qualityProfile?.trim()) saves.push(this.settingsService.update('Sonarr:DefaultQualityProfileName', { value: qualityProfile.trim() }));

    forkJoin(saves)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => { this.sonarrSaving.set(false); this.sonarrSaved.set(true); this.nextStep(); },
        error: (err: unknown) => {
          this.sonarrSaving.set(false);
          this.sonarrError.set(err instanceof HttpErrorResponse ? (err.error?.message ?? err.message) : 'Failed to save');
        },
      });
  }

  savePlexLibrariesAndNext(): void {
    const keys = this.plexSelectedLibKeys();
    if (keys.length > 0) {
      this.settingsService.update('Plex:AnimeSections', { value: keys.join(',') })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: () => this.settingsService.update('Plex:Enabled', { value: 'true' })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({ next: () => this.nextStep(), error: () => this.nextStep() }),
          error: () => this.nextStep(),
        });
    } else {
      this.nextStep();
    }
  }

  // Sync
  runSync(): void {
    if (this.syncing()) return;
    this.syncing.set(true);
    this.syncHttpError.set(null);
    // Tell SignalR which provider steps to exclude from the done check.
    // Kometa is never configured in the wizard; Plex and Sonarr are optional.
    const disabled = new Set<string>(['kometa']);
    if (!this.plexConnected()) disabled.add('plex');
    if (!this.sonarrSaved()) disabled.add('sonarr');
    this.signalR.setDisabledStepKeys(disabled);
    this.signalR.resetSteps();
    this.animeService.syncAnimes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {},
        error: (err: unknown) => {
          this.syncing.set(false);
          this.syncHttpError.set(err instanceof HttpErrorResponse ? (err.error?.message ?? err.message ?? 'Sync failed') : 'Sync failed');
          this.signalR.markError();
        },
      });
  }

  // Completion
  finish(): void { this.notifService.setWizardActive(false); this.providerState.refresh(); this.router.navigate(['/']); }

  private launchFireworks(): void {
    const duration = 3500;
    const end = Date.now() + duration;
    const colors = ['#6c63ff', '#a855f7', '#22c55e', '#f59e0b', '#ec4899', '#38bdf8'];
    const frame = () => {
      confetti({ particleCount: 6, angle: 60, spread: 55, origin: { x: 0, y: 1 }, colors, startVelocity: 45, ticks: 200, gravity: 0.85 });
      confetti({ particleCount: 6, angle: 120, spread: 55, origin: { x: 1, y: 1 }, colors, startVelocity: 45, ticks: 200, gravity: 0.85 });
      confetti({ particleCount: 4, angle: 90, spread: 70, origin: { x: 0.5, y: 1 }, colors, startVelocity: 50, ticks: 200, gravity: 0.85 });
      if (Date.now() < end) requestAnimationFrame(frame);
    };
    frame();
  }

  ngOnDestroy(): void {
    this.stopPlexPolling();
    if (this.malCallbackCopyTimer) clearTimeout(this.malCallbackCopyTimer);
    this.notifService.setWizardActive(false);
  }
}

import {
  Component,
  inject,
  signal,
  computed,
  DestroyRef,
  ChangeDetectionStrategy,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ActivatedRoute } from '@angular/router';
import { forkJoin, timer } from 'rxjs';
import { finalize } from 'rxjs/operators';
import { SettingsService } from '../services/settings.service';
import { AnimeService } from '../services/anime.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { NotificationService } from '../services/notification.service';
import { UserPreferencesService } from '../services/user-preferences.service';
import { SonarrService } from '../services/sonarr.service';
import { SonarrRootFolder, SonarrQualityProfile } from '../models/sonarr.models';
import { SettingDefinition, SettingsByCategory } from '../models/settings.models';
import { SyncStep } from '../models/sync-progress.models';
import { SyncStepComponent } from './sync-step/sync-step.component';
import { fmtElapsed } from '../utils/format.utils';

/** "Plex:Host" → "Plex_Host" for i18n key lookups */
function fieldSlug(key: string): string {
  return key.replace(':', '_');
}

/** Map an ngx-translate lang code to a BCP-47 locale for Intl.DateTimeFormat */
function langToLocale(lang: string): string {
  const map: Record<string, string> = {
    en: 'en-GB',
    fr: 'fr-FR',
    de: 'de-DE',
    es: 'es-ES',
    ja: 'ja-JP',
  };
  return map[lang] ?? lang;
}

@Component({
  selector: 'app-settings',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, ReactiveFormsModule, SyncStepComponent],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss',
})
export class SettingsComponent {
  private settingsService = inject(SettingsService);
  private animeService = inject(AnimeService);
  readonly signalR = inject(SyncSignalRService);
  private notifService = inject(NotificationService);
  private fb = inject(FormBuilder);
  private destroyRef = inject(DestroyRef);
  private translate = inject(TranslateService);
  private route = inject(ActivatedRoute);
  readonly userPrefs = inject(UserPreferencesService);
  private sonarrService = inject(SonarrService);

  loading = signal(true);
  error = signal(false);
  settings = signal<SettingDefinition[]>([]);
  savingKey = signal<string | null>(null);
  savedKey = signal<string | null>(null);
  errorKey = signal<string | null>(null);

  editing = signal<Record<string, boolean>>({});
  revealed = signal<Record<string, boolean>>({});

  autoApplySaved = signal(false);
  private autoApplySavedTimer: ReturnType<typeof setTimeout> | null = null;

  luckyPrefsSaved = signal(false);
  private luckyPrefsSavedTimer: ReturnType<typeof setTimeout> | null = null;

  sliderPreview = signal<Record<string, string | undefined>>({});

  syncing = signal(false);
  lastSyncDate = signal<string | null>(null);
  currentLang = signal(this.translate.currentLang ?? this.translate.defaultLang ?? 'en');

  syncSteps = toSignal(this.signalR.steps$, { initialValue: [] as SyncStep[] });
  syncDone = toSignal(this.signalR.syncDone$, { initialValue: false });
  syncError = toSignal(this.signalR.syncError$, { initialValue: false });
  hubConnected = toSignal(this.signalR.connected$, { initialValue: false });
  overallElapsed = toSignal(this.signalR.overallElapsedMs$, { initialValue: 0 });

  activeCategory = signal<string>('');
  highlighted = signal<string | null>(null);

  readonly providerImages: Record<string, string> = {
    plex: 'assets/images/plex.svg',
    sonarr: 'assets/images/sonarr.svg',
    mal: 'assets/images/mal.svg',
    kometa: 'assets/images/kometa.png',
    app: 'assets/images/app.svg',
    matching: 'assets/images/app.svg',
  };

  grouped = computed<SettingsByCategory>(() =>
    this.settings()
      .filter(
        (s) =>
          s.key !== 'App:SyncInterval' &&
          !s.key.startsWith('System:') &&
          !SettingsComponent.SONARR_DEFAULT_KEYS.has(s.key),
      )
      .reduce((acc, s) => {
        (acc[s.category] ??= []).push(s);
        return acc;
      }, {} as SettingsByCategory),
  );

  // ── Sonarr "Add Series Defaults" sub-group ──────────────────────────────
  sonarrRootFolders = signal<SonarrRootFolder[]>([]);
  sonarrQualityProfiles = signal<SonarrQualityProfile[]>([]);
  sonarrOptionsLoading = signal(false);
  sonarrOptionsError = signal('');

  sonarrBasicConfigured = computed(() => {
    const list = this.settings();
    const host = list.find((s) => s.key === SettingsComponent.SONARR_HOST_KEY)?.value ?? '';
    const apiKey = list.find((s) => s.key === SettingsComponent.SONARR_API_KEY_KEY)?.value ?? '';
    return host.trim().length > 0 && apiKey.trim().length > 0;
  });

  sonarrDefaultsUnlocked = computed(
    () =>
      this.sonarrBasicConfigured() &&
      !this.sonarrOptionsLoading() &&
      this.sonarrOptionsError() === '',
  );

  sonarrDefaultSettings = computed(() => {
    const order = [
      'Sonarr:DefaultRootFolder',
      'Sonarr:DefaultQualityProfileName',
      'Sonarr:DefaultSeasonFolder',
      'Sonarr:DefaultMonitor',
    ];
    return this.settings()
      .filter((s) => SettingsComponent.SONARR_DEFAULT_KEYS.has(s.key))
      .sort((a, b) => order.indexOf(a.key) - order.indexOf(b.key));
  });

  syncIntervalSetting = computed(() => this.settings().find((s) => s.key === 'App:SyncInterval'));

  malTokenRefreshDate = computed(
    () => this.settings().find((s) => s.key === 'System:MalTokenRefreshedAt')?.value ?? null,
  );

  categories = computed(() => Object.keys(this.grouped()));

  navItems = computed<string[]>(() => [...this.categories(), 'sync']);

  forms: Record<string, FormGroup> = {};

  readonly fmtElapsed = fmtElapsed;

  private static readonly HIGHLIGHT_DURATION_MS = 2000;
  private static readonly DEFAULT_NAVBAR_HEIGHT_PX = 56;
  private static readonly SCROLL_OFFSET_PADDING_PX = 8;
  private static readonly SAVED_STATE_TIMEOUT_MS = 2000;
  private static readonly SONARR_HOST_KEY = 'Sonarr:Host';
  private static readonly SONARR_API_KEY_KEY = 'Sonarr:ApiKey';
  private static readonly SONARR_DEFAULT_KEYS = new Set([
    'Sonarr:DefaultRootFolder',
    'Sonarr:DefaultQualityProfileName',
    'Sonarr:DefaultSeasonFolder',
    'Sonarr:DefaultMonitor',
  ]);

  /** Locale-aware date formatter — uses current app language */
  fmtDate(iso: string): string {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return iso;
    return d.toLocaleString(langToLocale(this.currentLang()), {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  hasSyncActivity = computed(() => this.syncSteps().some((s) => s.state !== 'pending'));

  sectionId(cat: string): string {
    return cat === 'sync' ? 'sync-section' : `section-${cat}`;
  }

  scrollToSection(cat: string): void {
    const el = document.getElementById(this.sectionId(cat));
    if (!el) return;
    this.activeCategory.set(cat);
    this.highlighted.set(cat);

    const navbar = document.querySelector('.navbar') as HTMLElement | null;
    const mobileSubnav = document.querySelector('.settings-mobile-subnav') as HTMLElement | null;
    const subnavVisible = mobileSubnav
      ? window.getComputedStyle(mobileSubnav).display !== 'none'
      : false;
    const offset =
      (navbar?.offsetHeight ?? SettingsComponent.DEFAULT_NAVBAR_HEIGHT_PX) +
      (subnavVisible ? (mobileSubnav?.offsetHeight ?? 0) : 0) +
      SettingsComponent.SCROLL_OFFSET_PADDING_PX;

    const y = el.getBoundingClientRect().top + window.scrollY - offset;
    window.scrollTo({ top: y, behavior: 'smooth' });

    setTimeout(() => {
      if (this.highlighted() === cat) this.highlighted.set(null);
    }, SettingsComponent.HIGHLIGHT_DURATION_MS);
  }

  constructor() {
    this.signalR.connect();

    this.destroyRef.onDestroy(() => {
      if (this.autoApplySavedTimer) clearTimeout(this.autoApplySavedTimer);
    });

    this.settingsService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.settings.set(data);
          this.buildForms(data);
          this.loading.set(false);
          const first = this.navItems()[0];
          if (first) this.activeCategory.set(first);
          const fragment = this.route.snapshot.fragment;
          if (fragment) {
            setTimeout(() => {
              document
                .getElementById(fragment)
                ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }, 80);
          }
          this.loadSonarrDefaults();
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
        },
      });

    this.loadSyncStatus();

    this.translate.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((evt) => this.currentLang.set(evt.lang));

    this.signalR.syncDone$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((done) => {
      if (done) {
        this.syncing.set(false);
        this.loadSyncStatus();
      }
    });
    this.signalR.syncError$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((err) => {
      if (err) this.syncing.set(false);
    });
  }

  private loadSyncStatus(): void {
    this.settingsService
      .getSyncStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (status) => this.lastSyncDate.set(status.lastSyncDate),
        error: () => {
          /* non-critical — ignore */
        },
      });
  }

  private buildForms(settings: SettingDefinition[]): void {
    const editingInit: Record<string, boolean> = {};
    const revealedInit: Record<string, boolean> = {};
    for (const s of settings) {
      this.forms[s.key] = this.fb.group({ value: [s.value ?? ''] });
      editingInit[s.key] = false;
      revealedInit[s.key] = false;
    }
    this.editing.set(editingInit);
    this.revealed.set(revealedInit);
  }

  slug(key: string): string {
    return fieldSlug(key);
  }

  toggleEdit(key: string): void {
    const nowEditing = !this.editing()[key];
    this.editing.update((e) => ({ ...e, [key]: nowEditing }));
    if (nowEditing) {
      if (this.savedKey() === key) this.savedKey.set(null);
      if (this.errorKey() === key) this.errorKey.set(null);
    }
  }

  toggleReveal(key: string): void {
    this.revealed.update((r) => ({ ...r, [key]: !r[key] }));
  }

  toggleBool(setting: SettingDefinition): void {
    const next = setting.value !== 'true' ? 'true' : 'false';
    this.savingKey.set(setting.key);
    this.settingsService
      .update(setting.key, { value: next })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.savingKey.set(null);
          this.savedKey.set(setting.key);
          this.settings.update((list) =>
            list.map((s) => (s.key === setting.key ? { ...s, value: next } : s)),
          );
          setTimeout(() => {
            if (this.savedKey() === setting.key) this.savedKey.set(null);
          }, 2000);
        },
        error: () => {
          this.savingKey.set(null);
        },
      });
  }

  boolValue(setting: SettingDefinition): boolean {
    return setting.value === 'true';
  }

  onSelectChange(setting: SettingDefinition, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.savingKey.set(setting.key);
    this.settingsService
      .update(setting.key, { value })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.savingKey.set(null);
          this.savedKey.set(setting.key);
          this.settings.update((list) =>
            list.map((s) => (s.key === setting.key ? { ...s, value } : s)),
          );
          setTimeout(() => {
            if (this.savedKey() === setting.key) this.savedKey.set(null);
          }, 2000);
        },
        error: () => {
          this.savingKey.set(null);
        },
      });
  }

  save(setting: SettingDefinition): void {
    const form = this.forms[setting.key];
    if (!form) return;

    const value: string = form.value.value ?? '';
    this.savingKey.set(setting.key);
    this.savedKey.set(null);
    this.errorKey.set(null);

    this.settingsService
      .update(setting.key, { value })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.savingKey.set(null);
          this.savedKey.set(setting.key);
          this.editing.update((e) => ({ ...e, [setting.key]: false }));
          this.settings.update((list) =>
            list.map((s) => (s.key === setting.key ? { ...s, value } : s)),
          );
          setTimeout(() => {
            if (this.savedKey() === setting.key) this.savedKey.set(null);
          }, 3000);
          if (
            setting.key === SettingsComponent.SONARR_HOST_KEY ||
            setting.key === SettingsComponent.SONARR_API_KEY_KEY
          ) {
            this.sonarrRootFolders.set([]);
            this.sonarrQualityProfiles.set([]);
            this.loadSonarrDefaults();
          }
        },
        error: () => {
          this.savingKey.set(null);
          this.errorKey.set(setting.key);
        },
      });
  }

  cancelEdit(key: string): void {
    this.editing.update((e) => ({ ...e, [key]: false }));
    this.revealed.update((r) => ({ ...r, [key]: false }));
    const setting = this.settings().find((s) => s.key === key);
    if (setting) {
      this.forms[key].patchValue({ value: setting.value ?? '' });
    }
  }

  isSaving(key: string): boolean {
    return this.savingKey() === key;
  }

  isSaved(key: string): boolean {
    return this.savedKey() === key;
  }

  hasError(key: string): boolean {
    return this.errorKey() === key;
  }

  protected sliderDisplayValue(key: string, fallback: string | null): string {
    return this.sliderPreview()[key] ?? fallback ?? '1';
  }

  updateSliderPreview(key: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.sliderPreview.update((p) => ({ ...p, [key]: value }));
  }

  onNumberChange(setting: SettingDefinition, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.savingKey.set(setting.key);

    forkJoin([
      this.settingsService.update(setting.key, { value }),
      timer(400)
    ])
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.savingKey.set(null);
        })
      )
      .subscribe({
        next: () => {
          this.savedKey.set(setting.key);
          this.settings.update((list) =>
            list.map((s) => (s.key === setting.key ? { ...s, value } : s)),
          );

          setTimeout(() => {
            if (this.savedKey() === setting.key) this.savedKey.set(null);
          }, 2000);
        },
        error: () => {
          /* finalize clears saving state */
        },
      });
  }

  toggleAutoApply(): void {
    this.userPrefs.setAutoApplyLastFilters(!this.userPrefs.autoApplyLastFilters());
    if (this.autoApplySavedTimer) clearTimeout(this.autoApplySavedTimer);
    this.autoApplySaved.set(true);
    this.autoApplySavedTimer = setTimeout(() => this.autoApplySaved.set(false), 2000);
  }

  toggleLuckyOnlyFinished(): void {
    this.userPrefs.setLuckyOnlyFinished(!this.userPrefs.luckyOnlyFinished());
    this.flashLuckyPrefs();
  }

  toggleLuckyOnlyPlanToWatch(): void {
    this.userPrefs.setLuckyOnlyPlanToWatch(!this.userPrefs.luckyOnlyPlanToWatch());
    this.flashLuckyPrefs();
  }

  toggleLuckyOnlyWithFiles(): void {
    this.userPrefs.setLuckyOnlyWithFiles(!this.userPrefs.luckyOnlyWithFiles());
    this.flashLuckyPrefs();
  }

  private flashLuckyPrefs(): void {
    if (this.luckyPrefsSavedTimer) clearTimeout(this.luckyPrefsSavedTimer);
    this.luckyPrefsSaved.set(true);
    this.luckyPrefsSavedTimer = setTimeout(() => this.luckyPrefsSaved.set(false), 2000);
  }

  // ── Sonarr defaults helpers ──────────────────────────────────────────────

  loadSonarrDefaults(): void {
    if (!this.sonarrBasicConfigured()) return;
    this.sonarrOptionsLoading.set(true);
    this.sonarrOptionsError.set('');
    this.sonarrService
      .getOptions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (opts) => {
          this.sonarrRootFolders.set(opts.rootFolders);
          this.sonarrQualityProfiles.set(opts.qualityProfiles);
          this.sonarrOptionsLoading.set(false);
        },
        error: () => {
          this.sonarrOptionsError.set(
            this.translate.instant('settings.sonarr.connectionError'),
          );
          this.sonarrOptionsLoading.set(false);
        },
      });
  }

  sonarrDefaultByKey(key: string): SettingDefinition | undefined {
    return this.settings().find((s) => s.key === key);
  }

  onSonarrDefaultChange(key: string, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.savingKey.set(key);
    this.savedKey.set(null);
    this.settingsService
      .update(key, { value })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.savingKey.set(null);
          this.savedKey.set(key);
          this.settings.update((list) =>
            list.map((s) => (s.key === key ? { ...s, value } : s)),
          );
          setTimeout(() => {
            if (this.savedKey() === key) this.savedKey.set(null);
          }, SettingsComponent.SAVED_STATE_TIMEOUT_MS);
        },
        error: () => {
          this.savingKey.set(null);
        },
      });
  }

  onSonarrDefaultBoolToggle(key: string): void {
    const current = this.settings().find((s) => s.key === key)?.value;
    const value = current !== 'true' ? 'true' : 'false';
    this.savingKey.set(key);
    this.settingsService
      .update(key, { value })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.savingKey.set(null);
          this.savedKey.set(key);
          this.settings.update((list) =>
            list.map((s) => (s.key === key ? { ...s, value } : s)),
          );
          setTimeout(() => {
            if (this.savedKey() === key) this.savedKey.set(null);
          }, SettingsComponent.SAVED_STATE_TIMEOUT_MS);
        },
        error: () => {
          this.savingKey.set(null);
        },
      });
  }

  trackByKey(_: number, s: SettingDefinition): string {
    return s.key;
  }

  trackByCategory(_: number, cat: string): string {
    return cat;
  }

  trackByStepKey(_: number, s: SyncStep): string {
    return s.stepKey;
  }

  triggerSync(): void {
    if (this.syncing()) return;

    this.syncing.set(true);
    this.signalR.resetSteps();

    this.notifService.push({
      id: `sync-start-${Date.now()}`,
      severity: 'info',
      titleKey: 'notifications.sync.startTitle',
      messageKey: 'notifications.sync.startMessage',
      action: {
        labelKey: 'notifications.sync.viewLog',
        route: ['/settings'],
        fragment: 'sync-section',
      },
    });

    const SYNC_SCROLL_DELAY_MS = 80;
    setTimeout(() => {
      document
        .getElementById('sync-section')
        ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }, SYNC_SCROLL_DELAY_MS);

    this.animeService
      .syncAnimes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          /* SignalR done event handles syncing flag */
        },
        error: () => {
          this.syncing.set(false);
          this.signalR.markError();
        },
      });
  }

  clearSyncLog(): void {
    this.signalR.resetSteps();
  }
}

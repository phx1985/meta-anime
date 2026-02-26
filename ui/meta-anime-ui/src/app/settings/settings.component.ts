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
import { SettingsService } from '../services/settings.service';
import { AnimeService } from '../services/anime.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { NotificationService } from '../services/notification.service';
import { UserPreferencesService } from '../services/user-preferences.service';
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

  // ── Settings load state ───────────────────────────────────────────────────
  loading = signal(true);
  error = signal(false);
  settings = signal<SettingDefinition[]>([]);
  savingKey = signal<string | null>(null);
  savedKey = signal<string | null>(null);
  errorKey = signal<string | null>(null);

  // ── Row-level edit / reveal state (signals — required in zoneless app) ────
  editing = signal<Record<string, boolean>>({});
  revealed = signal<Record<string, boolean>>({});

  // ── Auto-apply flash ──────────────────────────────────────────────────────
  autoApplySaved = signal(false);
  private autoApplySavedTimer: ReturnType<typeof setTimeout> | null = null;

  // ── Lucky Pick preferences flash ──────────────────────────────────────────
  luckyPrefsSaved = signal(false);
  private luckyPrefsSavedTimer: ReturnType<typeof setTimeout> | null = null;

  // ── Slider preview (tracks live drag value before persisting) ─────────────
  sliderPreview = signal<Record<string, string | undefined>>({});

  // ── Sync state (driven by SignalR) ────────────────────────────────────────
  syncing = signal(false);
  lastSyncDate = signal<string | null>(null);
  currentLang = signal(this.translate.currentLang ?? this.translate.defaultLang ?? 'en');

  // Convert SignalR observables → signals for template use
  syncSteps = toSignal(this.signalR.steps$, { initialValue: [] as SyncStep[] });
  syncDone = toSignal(this.signalR.syncDone$, { initialValue: false });
  syncError = toSignal(this.signalR.syncError$, { initialValue: false });
  hubConnected = toSignal(this.signalR.connected$, { initialValue: false });
  overallElapsed = toSignal(this.signalR.overallElapsedMs$, { initialValue: 0 });

  // ── Floating side navigation ──────────────────────────────────────────────
  /** Category currently in the top viewport slice (tracked by IntersectionObserver) */
  activeCategory = signal<string>('');
  /** Category recently clicked — gets a bounce highlight for 2 s then clears */
  highlighted = signal<string | null>(null);

  /** Provider logo paths — used both in category headers and in step rows */
  readonly providerImages: Record<string, string> = {
    plex: 'assets/images/plex.svg',
    sonarr: 'assets/images/sonarr.svg',
    mal: 'assets/images/mal.svg',
    kometa: 'assets/images/kometa.png',
    app: 'assets/images/app.svg',
    matching: 'assets/images/app.svg',
  };

  /** Settings grouped by category — App:SyncInterval and System:* are excluded */
  grouped = computed<SettingsByCategory>(() =>
    this.settings()
      .filter((s) => s.key !== 'App:SyncInterval' && !s.key.startsWith('System:'))
      .reduce((acc, s) => {
        (acc[s.category] ??= []).push(s);
        return acc;
      }, {} as SettingsByCategory),
  );

  /** The App:SyncInterval setting — rendered inside the sync card */
  syncIntervalSetting = computed(() => this.settings().find((s) => s.key === 'App:SyncInterval'));

  /** Last MAL OAuth token refresh date (from System:MalTokenRefreshedAt) */
  malTokenRefreshDate = computed(
    () => this.settings().find((s) => s.key === 'System:MalTokenRefreshedAt')?.value ?? null,
  );

  /** Ordered category keys */
  categories = computed(() => Object.keys(this.grouped()));

  /** All nav items: categories in order + 'sync' sentinel */
  navItems = computed<string[]>(() => [...this.categories(), 'sync']);

  /** Per-setting reactive forms */
  forms: Record<string, FormGroup> = {};

  /** Expose formatters to template */
  readonly fmtElapsed = fmtElapsed;

  // ── Timing / layout constants ──────────────────────────────────────────────
  private static readonly HIGHLIGHT_DURATION_MS = 2000;
  /** Fallback navbar height in px when `.navbar` cannot be measured */
  private static readonly DEFAULT_NAVBAR_HEIGHT_PX = 56;
  /** Extra breathing room above the scroll target */
  private static readonly SCROLL_OFFSET_PADDING_PX = 8;

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

  /** Whether the step log has any non-pending entries (i.e. a sync has been triggered) */
  hasSyncActivity = computed(() => this.syncSteps().some((s) => s.state !== 'pending'));

  /** DOM id for a nav category */
  sectionId(cat: string): string {
    return cat === 'sync' ? 'sync-section' : `section-${cat}`;
  }

  /** Scroll smoothly to a settings section, accounting for sticky navbar + mobile subnav.
   *  Sets `activeCategory` synchronously so the sidenav updates immediately on click. */
  scrollToSection(cat: string): void {
    const el = document.getElementById(this.sectionId(cat));
    if (!el) return;
    this.activeCategory.set(cat);
    this.highlighted.set(cat);

    // Measure sticky headers so their height is subtracted from scroll target
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
    // Connect to SignalR hub on component init
    this.signalR.connect();

    // Clear the auto-apply flash timer if the component is destroyed while it is running
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
          // Set active nav to first category on load
          const first = this.navItems()[0];
          if (first) this.activeCategory.set(first);
          // Scroll to fragment if present (e.g. from toast CTA)
          const fragment = this.route.snapshot.fragment;
          if (fragment) {
            setTimeout(() => {
              document
                .getElementById(fragment)
                ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }, 80);
          }
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
        },
      });

    // Load initial last sync date
    this.loadSyncStatus();

    // Track language changes so fmtDate() uses the correct locale
    this.translate.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((evt) => this.currentLang.set(evt.lang));

    // Auto-clear syncing flag when hub reports done/error; refresh sync date on done
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

  // ── Helpers ───────────────────────────────────────────────────────────────
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

  /** Immediately toggle a boolean setting (no edit-mode needed). */
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

  /** Whether a boolean setting is currently enabled */
  boolValue(setting: SettingDefinition): boolean {
    return setting.value === 'true';
  }

  /** Handle select-field change (auto-save on selection). */
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

  /** Live display value for a number/slider field — preview while dragging, then persisted value. */
  protected sliderDisplayValue(key: string, fallback: string | null): string {
    return this.sliderPreview()[key] ?? fallback ?? '1';
  }

  /** Update the live preview value as the slider thumb is dragged. */
  updateSliderPreview(key: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.sliderPreview.update((p) => ({ ...p, [key]: value }));
  }

  /** Auto-save a number setting when the slider interaction ends. */
  onNumberChange(setting: SettingDefinition, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
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

  /** Toggle auto-apply preference and briefly flash the setting row green. */
  toggleAutoApply(): void {
    this.userPrefs.setAutoApplyLastFilters(!this.userPrefs.autoApplyLastFilters());
    if (this.autoApplySavedTimer) clearTimeout(this.autoApplySavedTimer);
    this.autoApplySaved.set(true);
    this.autoApplySavedTimer = setTimeout(() => this.autoApplySaved.set(false), 2000);
  }

  /** Toggle Lucky Pick "only finished" preference. */
  toggleLuckyOnlyFinished(): void {
    this.userPrefs.setLuckyOnlyFinished(!this.userPrefs.luckyOnlyFinished());
    this.flashLuckyPrefs();
  }

  /** Toggle Lucky Pick "only plan to watch" preference. */
  toggleLuckyOnlyPlanToWatch(): void {
    this.userPrefs.setLuckyOnlyPlanToWatch(!this.userPrefs.luckyOnlyPlanToWatch());
    this.flashLuckyPrefs();
  }

  /** Toggle Lucky Pick "only with files" preference. */
  toggleLuckyOnlyWithFiles(): void {
    this.userPrefs.setLuckyOnlyWithFiles(!this.userPrefs.luckyOnlyWithFiles());
    this.flashLuckyPrefs();
  }

  private flashLuckyPrefs(): void {
    if (this.luckyPrefsSavedTimer) clearTimeout(this.luckyPrefsSavedTimer);
    this.luckyPrefsSaved.set(true);
    this.luckyPrefsSavedTimer = setTimeout(() => this.luckyPrefsSaved.set(false), 2000);
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

  // ── Sync card ─────────────────────────────────────────────────────────────
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

    // Scroll the sync card fully into view so the progress log is visible
    // Brief delay lets the sync card begin expanding before we scroll to it
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

import { Component, inject, signal, computed, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SettingsService } from '../services/settings.service';
import { AnimeService } from '../services/anime.service';
import { SyncSignalRService } from '../services/sync-signalr.service';
import { SettingDefinition, SettingsByCategory } from '../models/settings.models';
import { SyncStep } from '../models/sync-progress.models';

/** "Plex:Host" → "Plex_Host" for i18n key lookups */
function fieldSlug(key: string): string {
  return key.replace(':', '_');
}

/** Format elapsed ms as "1m 23s" or "45s" */
function fmtElapsed(ms: number): string {
  const s = Math.floor(ms / 1000);
  if (s < 60) return `${s}s`;
  return `${Math.floor(s / 60)}m ${s % 60}s`;
}

/** Map an ngx-translate lang code to a BCP-47 locale for Intl.DateTimeFormat */
function langToLocale(lang: string): string {
  const map: Record<string, string> = { en: 'en-GB', fr: 'fr-FR', de: 'de-DE', es: 'es-ES', ja: 'ja-JP' };
  return map[lang] ?? lang;
}

@Component({
  selector: 'app-settings',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, ReactiveFormsModule],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss'
})
export class SettingsComponent {
  private settingsService = inject(SettingsService);
  private animeService    = inject(AnimeService);
  readonly signalR        = inject(SyncSignalRService);
  private fb              = inject(FormBuilder);
  private destroyRef      = inject(DestroyRef);
  private translate       = inject(TranslateService);

  // ── Settings load state ───────────────────────────────────────────────────
  loading   = signal(true);
  error     = signal(false);
  settings  = signal<SettingDefinition[]>([]);
  savingKey = signal<string | null>(null);
  savedKey  = signal<string | null>(null);
  errorKey  = signal<string | null>(null);

  // ── Row-level edit / reveal state (signals — required in zoneless app) ────
  editing  = signal<Record<string, boolean>>({});
  revealed = signal<Record<string, boolean>>({});

  // ── Sync hub state (driven by SignalR) ────────────────────────────────────
  syncing      = signal(false);
  showSyncHub  = signal(false);
  lastSyncDate = signal<string | null>(null);
  currentLang  = signal(this.translate.currentLang ?? this.translate.defaultLang ?? 'en');

  // Convert SignalR observables → signals for template use
  syncSteps    = toSignal(this.signalR.steps$,     { initialValue: [] as SyncStep[] });
  syncDone     = toSignal(this.signalR.syncDone$,  { initialValue: false });
  syncError    = toSignal(this.signalR.syncError$,  { initialValue: false });
  hubConnected = toSignal(this.signalR.connected$,  { initialValue: false });

  /** Provider logo image paths */
  readonly providerImages: Record<string, string> = {
    plex:   'assets/images/plex.svg',
    sonarr: 'assets/images/sonarr.svg',
    mal:    'assets/images/mal.svg',
    kometa: 'assets/images/kometa.png',
    app:    'assets/images/app.svg',
  };

  /** Settings grouped by category */
  grouped = computed<SettingsByCategory>(() =>
    this.settings().reduce((acc, s) => {
      (acc[s.category] ??= []).push(s);
      return acc;
    }, {} as SettingsByCategory)
  );

  /** Ordered category keys */
  categories = computed(() => Object.keys(this.grouped()));

  /** Per-setting reactive forms */
  forms: Record<string, FormGroup> = {};

  /** Expose formatters to template */
  readonly fmtElapsed = fmtElapsed;

  /** Locale-aware date formatter — uses current app language */
  fmtDate(iso: string): string {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return iso;
    return d.toLocaleString(langToLocale(this.currentLang()), {
      year: 'numeric', month: 'short', day: 'numeric',
      hour: '2-digit', minute: '2-digit'
    });
  }

  constructor() {
    // Connect to SignalR hub on component init
    this.signalR.connect();

    this.settingsService.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: data => {
          this.settings.set(data);
          this.buildForms(data);
          this.loading.set(false);
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
        }
      });

    // Load initial last sync date
    this.loadSyncStatus();

    // Track language changes so fmtDate() uses the correct locale
    this.translate.onLangChange
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(evt => this.currentLang.set(evt.lang));

    // Auto-clear syncing flag when hub reports done/error; refresh sync date on done
    this.signalR.syncDone$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(done => {
      if (done) {
        this.syncing.set(false);
        this.loadSyncStatus();
      }
    });
    this.signalR.syncError$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(err => {
      if (err) this.syncing.set(false);
    });
  }

  private loadSyncStatus(): void {
    this.settingsService.getSyncStatus()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: status => this.lastSyncDate.set(status.lastSyncDate),
        error: () => { /* non-critical — ignore */ }
      });
  }

  private buildForms(settings: SettingDefinition[]): void {
    const editingInit:  Record<string, boolean> = {};
    const revealedInit: Record<string, boolean> = {};
    for (const s of settings) {
      // Pre-fill form with the real value (sensitive values now returned by API)
      this.forms[s.key]   = this.fb.group({ value: [s.value ?? ''] });
      editingInit[s.key]  = false;
      revealedInit[s.key] = false;
    }
    this.editing.set(editingInit);
    this.revealed.set(revealedInit);
  }

  // ── Helpers ───────────────────────────────────────────────────────────────
  slug(key: string): string { return fieldSlug(key); }

  toggleEdit(key: string): void {
    const nowEditing = !this.editing()[key];
    this.editing.update(e => ({ ...e, [key]: nowEditing }));
    if (nowEditing) {
      if (this.savedKey() === key) this.savedKey.set(null);
      if (this.errorKey() === key) this.errorKey.set(null);
    }
  }

  toggleReveal(key: string): void {
    this.revealed.update(r => ({ ...r, [key]: !r[key] }));
  }

  /** Immediately toggle a boolean setting (no edit-mode needed). */
  toggleBool(setting: SettingDefinition): void {
    const next = setting.value !== 'true' ? 'true' : 'false';
    this.savingKey.set(setting.key);
    this.settingsService.update(setting.key, { value: next })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.savingKey.set(null);
          this.settings.update(list =>
            list.map(s => s.key === setting.key ? { ...s, value: next } : s)
          );
        },
        error: () => { this.savingKey.set(null); }
      });
  }

  /** Whether a boolean setting is currently enabled */
  boolValue(setting: SettingDefinition): boolean {
    return setting.value === 'true';
  }

  save(setting: SettingDefinition): void {
    const form = this.forms[setting.key];
    if (!form) return;

    const value: string = form.value.value ?? '';
    this.savingKey.set(setting.key);
    this.savedKey.set(null);
    this.errorKey.set(null);

    this.settingsService.update(setting.key, { value })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.savingKey.set(null);
          this.savedKey.set(setting.key);
          this.editing.update(e => ({ ...e, [setting.key]: false }));
          this.settings.update(list =>
            list.map(s => s.key === setting.key ? { ...s, value } : s)
          );
          setTimeout(() => {
            if (this.savedKey() === setting.key) this.savedKey.set(null);
          }, 3000);
        },
        error: () => {
          this.savingKey.set(null);
          this.errorKey.set(setting.key);
        }
      });
  }

  cancelEdit(key: string): void {
    this.editing.update(e  => ({ ...e, [key]: false }));
    this.revealed.update(r => ({ ...r, [key]: false }));
    const setting = this.settings().find(s => s.key === key);
    if (setting) {
      this.forms[key].patchValue({ value: setting.value ?? '' });
    }
  }

  isSaving(key: string): boolean { return this.savingKey() === key; }
  isSaved(key: string):  boolean { return this.savedKey()  === key; }
  hasError(key: string): boolean { return this.errorKey()  === key; }

  trackByKey(_: number, s: SettingDefinition): string { return s.key; }
  trackByCategory(_: number, cat: string): string      { return cat;   }
  trackByStepKey(_: number, s: SyncStep): string       { return s.stepKey; }

  // ── Sync hub ──────────────────────────────────────────────────────────────
  triggerSync(): void {
    if (this.syncing()) return;

    this.syncing.set(true);
    this.showSyncHub.set(true);
    this.signalR.resetSteps();

    this.animeService.syncAnimes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        // SignalR drives the step UI; HTTP response is just confirmation
        next:  () => { /* SignalR done event handles syncing flag */ },
        error: () => { this.syncing.set(false); }
      });
  }

  dismissSyncHub(): void {
    this.showSyncHub.set(false);
    this.signalR.resetSteps();
  }
}

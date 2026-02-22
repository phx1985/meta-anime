import { Component, inject, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { SettingsService } from '../services/settings.service';
import { AnimeService } from '../services/anime.service';
import { SettingDefinition, SettingsByCategory } from '../models/settings.models';

/** Map a setting key (e.g. "Plex:Host") to an i18n slug (e.g. "Plex_Host") */
function fieldSlug(key: string): string {
  return key.replace(':', '_');
}

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [TranslatePipe, ReactiveFormsModule],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss'
})
export class SettingsComponent {
  private settingsService = inject(SettingsService);
  private animeService    = inject(AnimeService);
  private fb              = inject(FormBuilder);
  private destroyRef      = inject(DestroyRef);

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

  // ── Sync hub state ────────────────────────────────────────────────────────
  syncing      = signal(false);
  syncStep     = signal(-1);        // active step index, -1 = idle
  syncComplete = signal(false);
  syncError    = signal(false);
  showSyncHub  = signal(false);

  private stepTimers: ReturnType<typeof setTimeout>[] = [];

  /** Sync progress steps (i18n keys) */
  readonly syncSteps = [
    'settings.sync.step.init',
    'settings.sync.step.plex',
    'settings.sync.step.mal',
    'settings.sync.step.sonarr',
    'settings.sync.step.matching',
  ];

  /** Provider logo image paths */
  readonly providerImages: Record<string, string> = {
    plex:   'assets/images/plex.svg',
    sonarr: 'assets/images/sonarr.svg',
    mal:    'assets/images/mal.svg',
    kometa: 'assets/images/kometa.png',
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

  /** Per-setting reactive forms (populated once on load) */
  forms: Record<string, FormGroup> = {};

  constructor() {
    this.destroyRef.onDestroy(() => this.clearStepTimers());

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
  }

  private buildForms(settings: SettingDefinition[]): void {
    const editingInit:  Record<string, boolean> = {};
    const revealedInit: Record<string, boolean> = {};
    for (const s of settings) {
      this.forms[s.key]  = this.fb.group({ value: [s.isSensitive ? '' : (s.value ?? '')] });
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

  save(setting: SettingDefinition): void {
    const form = this.forms[setting.key];
    if (!form || form.invalid) return;

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
            list.map(s => s.key === setting.key
              ? { ...s, value: setting.isSensitive ? '••••••••' : value }
              : s)
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
    this.editing.update(e  => ({ ...e,  [key]: false }));
    this.revealed.update(r => ({ ...r,  [key]: false }));
    const setting = this.settings().find(s => s.key === key);
    if (setting) {
      this.forms[key].patchValue({ value: setting.isSensitive ? '' : (setting.value ?? '') });
    }
  }

  isSaving(key: string): boolean { return this.savingKey() === key; }
  isSaved(key: string):  boolean { return this.savedKey()  === key; }
  hasError(key: string): boolean { return this.errorKey()  === key; }

  trackByKey(_: number, s: SettingDefinition): string { return s.key; }
  trackByCategory(_: number, cat: string): string      { return cat;   }

  // ── Sync hub ──────────────────────────────────────────────────────────────
  /** Kick off a manual sync and animate step progress */
  triggerSync(): void {
    if (this.syncing()) return;

    this.syncing.set(true);
    this.syncComplete.set(false);
    this.syncError.set(false);
    this.syncStep.set(0);
    this.showSyncHub.set(true);
    this.clearStepTimers();

    // Advance through steps while waiting for the API
    const stepDelays = [5_000, 15_000, 30_000, 50_000]; // delays for steps 1-4
    stepDelays.forEach((delay, i) => {
      const t = setTimeout(() => {
        if (this.syncing()) this.syncStep.set(i + 1);
      }, delay);
      this.stepTimers.push(t);
    });

    this.animeService.syncAnimes()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.clearStepTimers();
          this.syncStep.set(this.syncSteps.length - 1);
          this.syncing.set(false);
          this.syncComplete.set(true);
          const t = setTimeout(() => this.dismissSyncHub(), 8_000);
          this.stepTimers.push(t);
        },
        error: () => {
          this.clearStepTimers();
          this.syncing.set(false);
          this.syncError.set(true);
        }
      });
  }

  dismissSyncHub(): void {
    this.showSyncHub.set(false);
    this.syncComplete.set(false);
    this.syncError.set(false);
    this.syncStep.set(-1);
  }

  stepState(i: number): 'pending' | 'running' | 'done' | 'error' {
    if (this.syncError()    && i === this.syncStep()) return 'error';
    if (this.syncComplete())                          return 'done';
    if (i < this.syncStep())                          return 'done';
    if (i === this.syncStep())                        return 'running';
    return 'pending';
  }

  private clearStepTimers(): void {
    this.stepTimers.forEach(clearTimeout);
    this.stepTimers = [];
  }
}


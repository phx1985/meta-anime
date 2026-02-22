import { Component, inject, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { SettingsService } from '../services/settings.service';
import { SettingDefinition, SettingsByCategory } from '../models/settings.models';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [TranslatePipe, ReactiveFormsModule],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss'
})
export class SettingsComponent {
  private settingsService = inject(SettingsService);
  private fb = inject(FormBuilder);
  private destroyRef = inject(DestroyRef);

  loading = signal(true);
  error = signal(false);
  settings = signal<SettingDefinition[]>([]);
  savingKey = signal<string | null>(null);
  savedKey = signal<string | null>(null);
  errorKey = signal<string | null>(null);

  /** Settings grouped by category */
  grouped = computed<SettingsByCategory>(() =>
    this.settings().reduce((acc, s) => {
      (acc[s.category] ??= []).push(s);
      return acc;
    }, {} as SettingsByCategory)
  );

  /** Ordered category keys */
  categories = computed(() => Object.keys(this.grouped()));

  /** Per-setting edit forms */
  forms: Record<string, FormGroup> = {};
  /** Track which fields are in "edit" mode */
  editing: Record<string, boolean> = {};

  constructor() {
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
    for (const s of settings) {
      this.forms[s.key] = this.fb.group({ value: [s.isSensitive ? '' : (s.value ?? '')] });
      this.editing[s.key] = false;
    }
  }

  toggleEdit(key: string): void {
    this.editing[key] = !this.editing[key];
    // Clear saved / error feedback when re-entering edit
    if (this.editing[key]) {
      if (this.savedKey() === key) this.savedKey.set(null);
      if (this.errorKey() === key) this.errorKey.set(null);
    }
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
          this.editing[setting.key] = false;
          // Update local value
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
    this.editing[key] = false;
    const setting = this.settings().find(s => s.key === key);
    if (setting) {
      this.forms[key].patchValue({ value: setting.isSensitive ? '' : (setting.value ?? '') });
    }
  }

  isSaving(key: string): boolean { return this.savingKey() === key; }
  isSaved(key: string): boolean  { return this.savedKey()  === key; }
  hasError(key: string): boolean { return this.errorKey()  === key; }

  trackByKey(_: number, s: SettingDefinition): string { return s.key; }
  trackByCategory(_: number, cat: string): string { return cat; }
}

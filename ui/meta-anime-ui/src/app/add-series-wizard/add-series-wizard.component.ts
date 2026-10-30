import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { UiSelectComponent, UiSelectOption } from '../shared/ui/ui-select/ui-select.component';
import { UiToggleComponent } from '../shared/ui/ui-toggle/ui-toggle.component';
import { SeasonalAnimeItem } from '../models/anime.models';
import {
  SonarrLookupResult,
  SonarrMonitorType,
  SonarrQualityProfile,
  SonarrRootFolder,
} from '../models/sonarr.models';

const SONARR_MONITOR_TYPES: readonly { value: SonarrMonitorType; labelKey: string }[] = [
  { value: 'all',               labelKey: 'seasonal.monitorTypes.all' },
  { value: 'future',            labelKey: 'seasonal.monitorTypes.future' },
  { value: 'missing',           labelKey: 'seasonal.monitorTypes.missing' },
  { value: 'existing',          labelKey: 'seasonal.monitorTypes.existing' },
  { value: 'recent',            labelKey: 'seasonal.monitorTypes.recent' },
  { value: 'pilot',             labelKey: 'seasonal.monitorTypes.pilot' },
  { value: 'firstSeason',       labelKey: 'seasonal.monitorTypes.firstSeason' },
  { value: 'lastSeason',        labelKey: 'seasonal.monitorTypes.lastSeason' },
  { value: 'monitorSpecials',   labelKey: 'seasonal.monitorTypes.monitorSpecials' },
  { value: 'unmonitorSpecials', labelKey: 'seasonal.monitorTypes.unmonitorSpecials' },
  { value: 'none',              labelKey: 'seasonal.monitorTypes.none' },
];

const MAL_STATUSES: readonly { value: string; labelKey: string }[] = [
  { value: 'watching',      labelKey: 'seasonal.malStatus.watching' },
  { value: 'completed',     labelKey: 'seasonal.malStatus.completed' },
  { value: 'on_hold',       labelKey: 'seasonal.malStatus.on_hold' },
  { value: 'dropped',       labelKey: 'seasonal.malStatus.dropped' },
  { value: 'plan_to_watch', labelKey: 'seasonal.malStatus.plan_to_watch' },
];

@Component({
  selector: 'app-add-series-wizard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, TranslatePipe, UiSelectComponent, UiToggleComponent],
  templateUrl: './add-series-wizard.component.html',
  styleUrl: './add-series-wizard.component.scss',
})
export class AddSeriesWizardComponent {
  private readonly translateService = inject(TranslateService);

  // ── Inputs ──────────────────────────────────────────────────────────────────
  showAddModal          = input.required<boolean>();
  addModalItem          = input.required<SeasonalAnimeItem | null>();
  addBusy               = input.required<boolean>();
  addError              = input.required<string>();
  addSuccess            = input.required<boolean>();
  addSonarr             = input.required<boolean>();
  addMal                = input.required<boolean>();
  expandedProvider      = input.required<'sonarr' | 'mal' | null>();
  sonarrRootFolders     = input.required<SonarrRootFolder[]>();
  sonarrQualityProfiles = input.required<SonarrQualityProfile[]>();
  sonarrLoadingOptions  = input.required<boolean>();
  sonarrSearchTerm      = input.required<string>();
  sonarrSearchResults   = input.required<SonarrLookupResult[]>();
  sonarrSearching       = input.required<boolean>();
  sonarrSearchError     = input.required<string>();
  sonarrSelected        = input.required<SonarrLookupResult | null>();
  sonarrRootFolder      = input.required<string>();
  sonarrProfileId       = input.required<number>();
  sonarrMonitor         = input.required<SonarrMonitorType>();
  sonarrSeasonFolder    = input.required<boolean>();
  addMalStatus          = input.required<string>();
  addMalEpisodes        = input.required<number>();
  addMalScore           = input.required<number>();
  sonarrEnabled         = input.required<boolean>();
  addModalInSonarr      = input.required<boolean>();
  addModalInMal         = input.required<boolean>();
  addModalAnythingSelected = input.required<boolean>();

  // ── Outputs ─────────────────────────────────────────────────────────────────
  closeRequested            = output<void>();
  confirmAddRequested       = output<void>();
  toggleProviderRequested   = output<'sonarr' | 'mal'>();
  toggleAccordionRequested  = output<'sonarr' | 'mal'>();
  sonarrSearchInputChanged  = output<string>();
  sonarrResultSelected      = output<SonarrLookupResult>();
  sonarrRootFolderChanged   = output<string>();
  sonarrProfileIdChanged    = output<number>();
  sonarrMonitorChanged      = output<string>();
  sonarrSeasonFolderChanged = output<boolean>();
  malStatusChanged          = output<string>();
  malEpisodesChanged        = output<number>();
  malScoreChanged           = output<number>();

  // ── Internal wizard state ────────────────────────────────────────────────────
  readonly currentStep = signal<1 | 2 | 3>(1);

  readonly wizardStep2Applicable = computed(
    () => this.addSonarr() && !this.addModalInSonarr(),
  );

  readonly sonarrMonitorTypes = SONARR_MONITOR_TYPES;
  readonly malStatuses = MAL_STATUSES;

  // ── ui-select option sets (labels translated here — the component takes final strings) ──

  sonarrRootFolderSelectOptions(): UiSelectOption[] {
    return this.sonarrRootFolders().map((f) => ({ label: f.path, value: f.path }));
  }

  sonarrProfileSelectOptions(): UiSelectOption[] {
    return this.sonarrQualityProfiles().map((p) => ({ label: p.name, value: p.id }));
  }

  sonarrMonitorSelectOptions(): UiSelectOption[] {
    return this.sonarrMonitorTypes.map((m) => ({
      label: this.translateService.instant(m.labelKey),
      value: m.value,
    }));
  }

  malStatusSelectOptions(): UiSelectOption[] {
    return this.malStatuses.map((s) => ({
      label: this.translateService.instant(s.labelKey),
      value: s.value,
    }));
  }

  // Reset step to 1 each time the modal opens
  constructor() {
    effect(() => {
      if (this.showAddModal()) {
        this.currentStep.set(1);
      }
    });
  }

  // ── Computed helpers exposed to template ────────────────────────────────────
  readonly coverImageUrl = computed(() => this.addModalItem()?.coverImageUrl);
  readonly displayTitle  = computed(() => this.addModalItem()?.title ?? '');

  // ── Navigation ───────────────────────────────────────────────────────────────
  canGoNext(): boolean {
    const step = this.currentStep();
    if (step === 1) return this.addModalAnythingSelected();
    if (step === 2) return this.sonarrSelected() !== null;
    return false;
  }

  goNext(): void {
    const step = this.currentStep();
    if (step === 1) {
      this.currentStep.set(this.wizardStep2Applicable() ? 2 : 3);
    } else if (step === 2) {
      this.currentStep.set(3);
    }
  }

  goBack(): void {
    const step = this.currentStep();
    if (step === 3) {
      this.currentStep.set(this.wizardStep2Applicable() ? 2 : 1);
    } else if (step === 2) {
      this.currentStep.set(1);
    }
  }

  // ── Event forwarders ─────────────────────────────────────────────────────────
  onSearchInput(term: string): void {
    this.sonarrSearchInputChanged.emit(term);
  }

  onResultSelect(item: SonarrLookupResult): void {
    this.sonarrResultSelected.emit(item);
  }

  onRootFolderChange(path: string): void {
    this.sonarrRootFolderChanged.emit(path);
  }

  onProfileIdChange(id: string): void {
    this.sonarrProfileIdChanged.emit(+id);
  }

  onMonitorChange(val: string): void {
    this.sonarrMonitorChanged.emit(val);
  }

  onSeasonFolderChange(val: boolean): void {
    this.sonarrSeasonFolderChanged.emit(val);
  }

  onMalStatusChange(val: string): void {
    this.malStatusChanged.emit(val);
  }

  onMalEpisodesChange(val: string): void {
    this.malEpisodesChanged.emit(+val);
  }

  onMalScoreChange(val: string): void {
    this.malScoreChanged.emit(+val);
  }
}

import { Component, Input, signal, inject, HostListener, HostBinding, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { trigger, style, animate, transition } from '@angular/animations';
import { PopupService } from '../../services/popup.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DestroyRef } from '@angular/core';
import { SeasonalAnimeItem } from '../../models/anime.models';
import { ScoreBadgeComponent } from '../../anime-list/anime-card/score-badge/score-badge.component';
import { GENRE_LIST, THEME_LIST, DEMOGRAPHIC_LIST } from '../../constants/genre-lists';
import { SonarrService } from '../../services/sonarr.service';
import { AnimeService } from '../../services/anime.service';
import { SettingsService } from '../../services/settings.service';
import {
  SonarrRootFolder,
  SonarrQualityProfile,
  SonarrLookupResult,
  SonarrAddResult,
  SonarrMonitorType,
} from '../../models/sonarr.models';
import { Subject, debounceTime, switchMap, of, catchError, forkJoin } from 'rxjs';

@Component({
  selector: 'app-seasonal-card',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe, ScoreBadgeComponent],
  templateUrl: './seasonal-card.component.html',
  styleUrl: './seasonal-card.component.scss',
  animations: [
    trigger('accordionExpand', [
      transition(':enter', [
        style({ height: '0', opacity: '0', overflow: 'hidden' }),
        animate('280ms cubic-bezier(0.4, 0, 0.2, 1)', style({ height: '*', opacity: '1' })),
      ]),
      transition(':leave', [
        style({ overflow: 'hidden' }),
        animate('200ms cubic-bezier(0.4, 0, 1, 1)', style({ height: '0', opacity: '0' })),
      ]),
    ]),
  ],
})
export class SeasonalCardComponent implements OnInit {
  @Input({ required: true }) anime!: SeasonalAnimeItem;
  /** Genres selected in the parent filter — highlights matching genre tags on the card. */
  @Input() activeGenres: string[] = [];
  @Input() activeThemes: string[] = [];
  @Input() activeDemographics: string[] = [];
  /** Whether the Plex provider is enabled globally. */
  @Input() plexEnabled = true;
  /** Whether the Sonarr provider is enabled globally. */
  @Input() sonarrEnabled = true;

  private translate = inject(TranslateService);
  private sonarrService = inject(SonarrService);
  private animeService = inject(AnimeService);
  private settingsService = inject(SettingsService);
  private destroyRef = inject(DestroyRef);
  private popupService = inject(PopupService);

  showLightbox = signal(false);

  // ── Neon aura animation signal (set true after a successful add) ──────────
  /** Triggers the electric-blue plasma aura on the card after providers are added. */
  neonGlow = signal(false);

  /** Host class binding — drives the neon aura via :host.neon-glow-active::after pseudo-element. */
  @HostBinding('class.neon-glow-active')
  get neonGlowActive(): boolean {
    return this.neonGlow();
  }

  /**
   * Elevate the card's stacking context above sibling cards when a modal or lightbox is open.
   * Without this, cards later in the DOM (same z-index: 0 stacking context) paint over the
   * fixed dialog-backdrop, because the backdrop is inside the card's own stacking context.
   */
  @HostBinding('style.zIndex')
  get hostZIndex(): string | null {
    return this.showAddModal() || this.showLightbox() ? '1090' : null;
  }

  // ── Unified add modal ────────────────────────────────────────────────────
  showAddModal = signal(false);
  addBusy = signal(false);
  addError = signal('');
  addSuccess = signal(false);
  addSonarr = signal(false);
  addMal = signal(false);
  lastAddedSonarrId = signal<number | null>(null);

  /** Which provider accordion is currently expanded (null = all collapsed). */
  expandedProvider = signal<'sonarr' | 'mal' | null>(null);

  // ── Sonarr add state ─────────────────────────────────────────────────────
  sonarrRootFolders = signal<SonarrRootFolder[]>([]);
  sonarrQualityProfiles = signal<SonarrQualityProfile[]>([]);
  sonarrLoadingOptions = signal(false);
  sonarrSearchTerm = signal('');
  sonarrSearchResults = signal<SonarrLookupResult[]>([]);
  sonarrSearching = signal(false);
  sonarrSearchError = signal('');
  sonarrSelected = signal<SonarrLookupResult | null>(null);
  sonarrRootFolder = signal('');
  sonarrProfileId = signal(0);
  sonarrMonitor = signal<SonarrMonitorType>('all');
  sonarrSeasonFolder = signal(true);

  readonly sonarrMonitorTypes: { value: SonarrMonitorType; labelKey: string }[] = [
    { value: 'all', labelKey: 'seasonal.monitorTypes.all' },
    { value: 'future', labelKey: 'seasonal.monitorTypes.future' },
    { value: 'missing', labelKey: 'seasonal.monitorTypes.missing' },
    { value: 'existing', labelKey: 'seasonal.monitorTypes.existing' },
    { value: 'recent', labelKey: 'seasonal.monitorTypes.recent' },
    { value: 'pilot', labelKey: 'seasonal.monitorTypes.pilot' },
    { value: 'firstSeason', labelKey: 'seasonal.monitorTypes.firstSeason' },
    { value: 'lastSeason', labelKey: 'seasonal.monitorTypes.lastSeason' },
    { value: 'monitorSpecials', labelKey: 'seasonal.monitorTypes.monitorSpecials' },
    { value: 'unmonitorSpecials', labelKey: 'seasonal.monitorTypes.unmonitorSpecials' },
    { value: 'none', labelKey: 'seasonal.monitorTypes.none' },
  ];

  private sonarrSearch$ = new Subject<string>();

  // ── MAL add state ─────────────────────────────────────────────────────────
  malStatus = signal('plan_to_watch');
  malEpisodes = signal(0);
  malScore = signal(0);

  readonly malStatuses = [
    { value: 'watching', labelKey: 'seasonal.malStatus.watching' },
    { value: 'completed', labelKey: 'seasonal.malStatus.completed' },
    { value: 'on_hold', labelKey: 'seasonal.malStatus.on_hold' },
    { value: 'dropped', labelKey: 'seasonal.malStatus.dropped' },
    { value: 'plan_to_watch', labelKey: 'seasonal.malStatus.plan_to_watch' },
  ];

  ngOnInit(): void {
    this.sonarrSearch$
      .pipe(
        debounceTime(350),
        // NOTE: distinctUntilChanged intentionally removed — it blocked re-searches
        // when re-opening the modal for the same anime (same term → no new emission).
        switchMap((term) => {
          if (!term.trim()) return of([]);
          this.sonarrSearching.set(true);
          this.sonarrSearchError.set('');
          return this.sonarrService.lookup(term).pipe(
            catchError(() => {
              this.sonarrSearchError.set(this.translate.instant('seasonal.sonarrSearchError'));
              return of([]);
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((results) => {
        this.sonarrSearchResults.set(results);
        this.sonarrSearching.set(false);
        // Auto-select when the user searched by TVDB ID and there is exactly one result
        if (this.sonarrSearchTerm().startsWith('tvdb:') && results.length === 1 && !results[0].alreadyInLibrary) {
          this.sonarrSelected.set(results[0]);
        }
      });
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.showLightbox()) {
      this.showLightbox.set(false);
      return;
    }
    if (this.showAddModal()) {
      this.closeAddModal();
    }
  }

  get coverImageUrl(): string | undefined {
    return this.anime.coverImageUrl;
  }
  get displayTitle(): string {
    return this.anime.title;
  }
  get description(): string {
    return this.anime.synopsis ?? '';
  }

  private get allMalGenres(): string[] {
    return this.anime.genres ?? [];
  }
  /** MAL genre-category entries only. */
  get pureGenres(): string[] {
    return this.allMalGenres.filter((g) => GENRE_LIST.includes(g));
  }
  /** MAL theme entries only. */
  get themes(): string[] {
    return this.allMalGenres.filter((g) => THEME_LIST.includes(g));
  }
  /** MAL demographic entries only. */
  get demographics(): string[] {
    return this.allMalGenres.filter((g) => DEMOGRAPHIC_LIST.includes(g));
  }
  /** @deprecated kept for any external references */
  get genres(): string[] {
    return this.allMalGenres;
  }

  get displayMalScore(): number {
    return this.anime.malScore ?? 0;
  }
  get startDate(): string | undefined {
    return this.anime.startDate;
  }
  get mediaType(): string | undefined {
    return this.anime.mediaType;
  }
  get seriesStatus(): string | undefined {
    return this.anime.status;
  }
  get malUrl(): string | null {
    return this.anime.malUrl ?? null;
  }

  /** True when the anime is tracked in both Sonarr and MAL. */
  get allTracked(): boolean {
    return this.anime.inSonarr && this.anime.inMal;
  }

  /** True when the anime is tracked in exactly one of Sonarr / MAL (but not both). */
  get partiallyTracked(): boolean {
    return this.anime.inSonarr !== this.anime.inMal;
  }

  /** True when at least one writable provider does not have this anime. */
  get anyMissing(): boolean {
    return !this.anime.inSonarr || !this.anime.inMal;
  }

  /** True when Sonarr is the only missing provider (MAL has it, Sonarr does not). */
  get sonarrMissing(): boolean {
    return this.partiallyTracked && !this.anime.inSonarr;
  }

  /** True when MAL is the only missing provider (Sonarr has it, MAL does not). */
  get malMissing(): boolean {
    return this.partiallyTracked && !this.anime.inMal;
  }

  translateGenre(name: string): string {
    const key = `genres.${name}`;
    const t = this.translate.instant(key);
    return t !== key ? t : name;
  }

  mediaTypeLabel(type: string): string {
    const key = `animeCard.mediaType.${type}`;
    const t = this.translate.instant(key);
    return t !== key ? t : type.toUpperCase();
  }

  seriesStatusLabel(status: string): string {
    const key = `animeCard.seriesStatus.${status}`;
    const t = this.translate.instant(key);
    return t !== key ? t : status.replace(/_/g, ' ');
  }

  openMalPage(): void {
    if (this.malUrl) window.open(this.malUrl, '_blank', 'noopener,noreferrer');
  }

  openSynopsisModal(): void {
    this.popupService.openSynopsis(this.displayTitle, this.description);
  }
  openLightbox(): void {
    if (this.coverImageUrl) this.showLightbox.set(true);
  }
  closeLightbox(): void {
    this.showLightbox.set(false);
  }

  // ── Unified add modal ─────────────────────────────────────────────────────

  openAddModal(): void {
    // Only pre-select Sonarr if it is both missing AND enabled globally
    this.addSonarr.set(!this.anime.inSonarr && this.sonarrEnabled);
    this.addMal.set(!this.anime.inMal);
    this.addError.set('');
    this.addSuccess.set(false);
    this.lastAddedSonarrId.set(null);
    this.malStatus.set('plan_to_watch');
    this.malEpisodes.set(0);
    this.malScore.set(0);

    // Auto-expand the first missing provider (skip Sonarr if disabled)
    if (!this.anime.inSonarr && this.sonarrEnabled) {
      this.expandedProvider.set('sonarr');
    } else if (!this.anime.inMal) {
      this.expandedProvider.set('mal');
    } else {
      this.expandedProvider.set(null);
    }

    this.showAddModal.set(true);

    // Kick off Sonarr search + options when Sonarr is pre-selected and enabled
    if (!this.anime.inSonarr && this.sonarrEnabled) {
      this.initSonarrSection();
    }
  }

  closeAddModal(): void {
    this.showAddModal.set(false);
    this.expandedProvider.set(null);
  }

  /**
   * Toggles the accordion for the given provider (exclusive — closes others).
   * If the provider is locked (already in library), this is a no-op.
   */
  toggleAccordion(provider: 'sonarr' | 'mal'): void {
    const next = this.expandedProvider() === provider ? null : provider;
    this.expandedProvider.set(next);
    // If expanding Sonarr while it is enabled, ensure search is running
    if (next === 'sonarr' && this.addSonarr()) {
      this.ensureSonarrSearch();
    }
  }

  /**
   * Toggles the add-toggle for a provider.
   * When turning ON, also expands that provider's accordion and triggers init.
   */
  toggleProvider(provider: 'sonarr' | 'mal'): void {
    if (provider === 'sonarr') {
      const next = !this.addSonarr();
      this.addSonarr.set(next);
      if (next) {
        this.expandedProvider.set('sonarr');
        this.initSonarrSection();
      }
    } else {
      const next = !this.addMal();
      this.addMal.set(next);
      if (next) this.expandedProvider.set('mal');
    }
  }

  onSonarrSearchInput(term: string): void {
    this.sonarrSearchTerm.set(term);
    this.sonarrSelected.set(null);
    this.sonarrSearch$.next(term);
  }

  selectSonarrResult(item: SonarrLookupResult): void {
    if (item.alreadyInLibrary) return;
    this.sonarrSelected.set(item);
  }

  get anythingSelected(): boolean {
    return (!this.anime.inSonarr && this.addSonarr()) || (!this.anime.inMal && this.addMal());
  }

  confirmAdd(): void {
    if (this.addBusy() || !this.anythingSelected) return;
    const willAddSonarr = !this.anime.inSonarr && this.addSonarr();
    const willAddMal = !this.anime.inMal && this.addMal();
    this.addBusy.set(true);
    this.addError.set('');
    this.doAddSonarr(() => {
      this.doAddMal(() => {
        this.addSuccess.set(true);
        this.addBusy.set(false);
        setTimeout(() => {
          this.closeAddModal();
          if (willAddSonarr || willAddMal) {
            this.neonGlow.set(true);
            setTimeout(() => this.neonGlow.set(false), 2900);
          }
        }, 1500);
      });
    });
  }

  /** Returns the best search term for Sonarr lookup:
   *  1. `tvdb:XXXXX` when TVDB ID is known (exact match in Sonarr)
   *  2. English title if available
   *  3. Default title */
  private getSonarrSearchTerm(): string {
    if (this.anime.tvdbId) return `tvdb:${this.anime.tvdbId}`;
    if (this.anime.englishTitle?.trim()) return this.anime.englishTitle.trim();
    return this.anime.title;
  }

  /** Initialises the Sonarr search term + results and loads options from scratch. */
  private initSonarrSection(): void {
    const term = this.getSonarrSearchTerm();
    this.sonarrSearchTerm.set(term);
    this.sonarrSelected.set(null);
    this.sonarrSearchResults.set([]);
    this.sonarrSearchError.set('');
    // Emit an empty string first so debounce sees a value change, then the real term.
    // This ensures the search fires even if the Subject last emitted the same term.
    this.sonarrSearch$.next('');
    this.sonarrSearch$.next(term);
    this.loadSonarrOptions();
  }

  /** Ensures a Sonarr search is running when results are absent. */
  private ensureSonarrSearch(): void {
    if (this.sonarrSearchResults().length === 0 && !this.sonarrSearching()) {
      const term = this.getSonarrSearchTerm();
      this.sonarrSearchTerm.set(term);
      this.sonarrSearch$.next('');
      this.sonarrSearch$.next(term);
    }
    this.loadSonarrOptions();
  }

  /** Loads Sonarr root folders + quality profiles if not already cached. */
  private loadSonarrOptions(): void {
    if (this.sonarrRootFolders().length > 0 || this.sonarrLoadingOptions()) return;
    this.sonarrLoadingOptions.set(true);
    forkJoin({
      opts: this.sonarrService.getOptions(),
      settings: this.settingsService.getAll(),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ opts, settings }) => {
          this.sonarrRootFolders.set(opts.rootFolders);
          this.sonarrQualityProfiles.set(opts.qualityProfiles);

          const getDefault = (key: string) => settings.find((s) => s.key === key)?.value ?? '';
          const defaultRootFolder = getDefault('Sonarr:DefaultRootFolder');
          const defaultProfileName = getDefault('Sonarr:DefaultQualityProfileName');
          const defaultSeasonFolder = getDefault('Sonarr:DefaultSeasonFolder');
          const defaultMonitor = getDefault('Sonarr:DefaultMonitor');

          const folder = (defaultRootFolder
            ? opts.rootFolders.find((f) => f.path === defaultRootFolder)
            : null) ?? opts.rootFolders[0];
          if (folder) this.sonarrRootFolder.set(folder.path);

          const profile = (defaultProfileName
            ? opts.qualityProfiles.find((p) => p.name === defaultProfileName)
            : null) ?? opts.qualityProfiles[0];
          if (profile) this.sonarrProfileId.set(profile.id);

          if (defaultSeasonFolder) this.sonarrSeasonFolder.set(defaultSeasonFolder === 'true');
          if (defaultMonitor && this.sonarrMonitorTypes.some((t) => t.value === defaultMonitor)) {
            this.sonarrMonitor.set(defaultMonitor as SonarrMonitorType);
          }

          this.sonarrLoadingOptions.set(false);
        },
        error: () => {
          this.addError.set(this.translate.instant('seasonal.sonarrOptionsError'));
          this.sonarrLoadingOptions.set(false);
        },
      });
  }

  private doAddSonarr(onSuccess: () => void): void {
    if (this.anime.inSonarr || !this.addSonarr()) {
      onSuccess();
      return;
    }

    const selected = this.sonarrSelected();
    if (!selected) {
      this.addError.set(this.translate.instant('seasonal.sonarrNoneSelected'));
      this.addBusy.set(false);
      return;
    }

    this.sonarrService
      .addSeries({
        tvdbId: selected.tvdbId,
        rootFolderPath: this.sonarrRootFolder(),
        qualityProfileId: this.sonarrProfileId(),
        monitor: this.sonarrMonitor(),
        seasonFolder: this.sonarrSeasonFolder(),
        malId: this.anime.malId,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result: SonarrAddResult) => {
          this.lastAddedSonarrId.set(result.sonarrId);
          (this.anime as { inSonarr: boolean }).inSonarr = true;
          if (result.sonarrUrl) (this.anime as { sonarrUrl?: string }).sonarrUrl = result.sonarrUrl;
          onSuccess();
        },
        error: (err) => {
          this.addError.set(this._extractErrorMessage(err, 'seasonal.sonarrAddError'));
          this.addBusy.set(false);
        },
      });
  }

  private doAddMal(onSuccess: () => void): void {
    if (this.anime.inMal || !this.addMal()) {
      onSuccess();
      return;
    }

    this.animeService
      .updateMalListStatus(this.anime.malId, {
        status: this.malStatus(),
        numWatchedEpisodes: this.malEpisodes(),
        score: this.malScore(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          (this.anime as { inMal: boolean }).inMal = true;
          onSuccess();
        },
        error: (err) => {
          const sonarrId = this.lastAddedSonarrId();
          if (sonarrId !== null) {
            this.sonarrService
              .removeSeries(sonarrId)
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe({
                next: () => {
                  (this.anime as { inSonarr: boolean }).inSonarr = false;
                  (this.anime as { sonarrUrl?: string }).sonarrUrl = undefined;
                  this.lastAddedSonarrId.set(null);
                },
                error: () => {
                  (this.anime as { inSonarr: boolean }).inSonarr = false;
                  this.lastAddedSonarrId.set(null);
                },
              });
          }
          this.addError.set(this._extractErrorMessage(err, 'seasonal.malAddError'));
          this.addBusy.set(false);
        },
      });
  }

  /**
   * Safely extracts a human-readable message from an Angular HttpErrorResponse.
   * Handles:
   *  - string body  (our backend returns StatusCode(500, message string))
   *  - Sonarr-format array  [{errorCode, errorMessage}]
   *  - ASP.NET problem-details object  {message | title}
   *  - Fallback to translated key
   */
  private _extractErrorMessage(err: unknown, fallbackKey: string): string {
    const e = err as Record<string, unknown> | null | undefined;
    if (!e) return this.translate.instant(fallbackKey);
    const body = e['error'];
    if (typeof body === 'string' && body.length > 0) return body;
    if (Array.isArray(body) && body.length > 0) {
      const first = body[0];
      if (first !== null && typeof first === 'object') {
        const obj = first as Record<string, unknown>;
        const msg = obj['errorMessage'] ?? obj['message'];
        if (typeof msg === 'string') return msg;
      }
    }
    if (body && typeof body === 'object') {
      const obj = body as Record<string, unknown>;
      const msg = obj['message'] ?? obj['title'] ?? obj['errorMessage'];
      if (typeof msg === 'string') return msg;
    }
    return this.translate.instant(fallbackKey);
  }
}

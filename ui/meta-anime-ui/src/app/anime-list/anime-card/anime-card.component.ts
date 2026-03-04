import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  HostBinding,
  HostListener,
  OnInit,
  input,
  output,
  signal,
  inject,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { trigger, style, animate, transition } from '@angular/animations';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, debounceTime, switchMap, of, catchError, forkJoin } from 'rxjs';
import { AnimeDetailsResponse, MalAnimeInfo, SeasonalAnimeItem, SonarrAnimeInfo } from '../../models/anime.models';
import { ScoreBadgeComponent } from './score-badge/score-badge.component';
import { GENRE_LIST, THEME_LIST, DEMOGRAPHIC_LIST } from '../../constants/genre-lists';
import { SonarrService } from '../../services/sonarr.service';
import { AnimeService } from '../../services/anime.service';
import { SettingsService } from '../../services/settings.service';
import { PopupService } from '../../services/popup.service';
import { PlexDeepLinkService } from '../../services/plex-deep-link.service';
import { FranchiseConstellationService } from '../../services/franchise-constellation.service';
import {
  SonarrRootFolder,
  SonarrQualityProfile,
  SonarrLookupResult,
  SonarrAddResult,
  SonarrMonitorType,
} from '../../models/sonarr.models';

@Component({
  selector: 'app-anime-card',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, ScoreBadgeComponent, DatePipe, FormsModule],
  templateUrl: './anime-card.component.html',
  styleUrl: './anime-card.component.scss',
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
export class AnimeCardComponent implements OnInit {
  readonly anime = input.required<AnimeDetailsResponse>();
  /** Emitted when the user clicks the card body to open the detail panel. */
  readonly openDetails = output<string>();
  /** When true, the first-time onboarding hints are shown on this card. */
  readonly showOnboardingHint = input(false);
  /** Emitted when the user dismisses the onboarding hint by interacting with the card. */
  readonly onboardingDismissed = output<void>();

  // ── Seasonal mode inputs ──────────────────────────────────────────────────
  readonly seasonalItem = input<SeasonalAnimeItem | null>(null);
  readonly showSeasonalAdd = input(false);
  readonly sonarrEnabled = input(false);
  readonly plexEnabled = input(false);

  // ── Seasonal mode output ──────────────────────────────────────────────────
  readonly openDetailsByMalId = output<string>();

  private translate = inject(TranslateService);
  private sonarrService = inject(SonarrService);
  private animeService = inject(AnimeService);
  private settingsService = inject(SettingsService);
  private destroyRef = inject(DestroyRef);
  private popupService = inject(PopupService);
  private plexDeepLink = inject(PlexDeepLinkService);
  protected franchiseConstellation = inject(FranchiseConstellationService);

  showAltTitles = signal(false);

  // ── Neon aura animation signal (set true after a successful add) ──────────
  neonGlow = signal(false);

  @HostBinding('class.neon-glow-active')
  get neonGlowActive(): boolean {
    return this.neonGlow();
  }

  @HostBinding('style.zIndex')
  get hostZIndex(): string | null {
    return this.showAddModal() || this.showLightbox() ? '1090' : null;
  }

  showLightbox = signal(false);

  // ── Unified add modal ─────────────────────────────────────────────────────
  showAddModal = signal(false);
  addBusy = signal(false);
  addError = signal('');
  addSuccess = signal(false);
  addSonarr = signal(false);
  addMal = signal(false);
  lastAddedSonarrId = signal<number | null>(null);

  /** Which provider accordion is currently expanded (null = all collapsed). */
  expandedProvider = signal<'sonarr' | 'mal' | null>(null);

  // ── Sonarr add state ──────────────────────────────────────────────────────
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
  addMalStatus = signal('plan_to_watch');
  addMalEpisodes = signal(0);
  addMalScore = signal(0);

  readonly malStatuses = [
    { value: 'watching', labelKey: 'seasonal.malStatus.watching' },
    { value: 'completed', labelKey: 'seasonal.malStatus.completed' },
    { value: 'on_hold', labelKey: 'seasonal.malStatus.on_hold' },
    { value: 'dropped', labelKey: 'seasonal.malStatus.dropped' },
    { value: 'plan_to_watch', labelKey: 'seasonal.malStatus.plan_to_watch' },
  ];

  // ── Lifecycle ─────────────────────────────────────────────────────────────

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

    // Ensure the global add-modal flag is cleared if this card is destroyed while the modal is open
    this.destroyRef.onDestroy(() => this.popupService.addProviderModalOpen.set(false));
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

  // ── Data accessors ────────────────────────────────────────────────────────

  get mal(): MalAnimeInfo | undefined {
    return this.anime().malAnimes?.[0];
  }

  get sonarr(): SonarrAnimeInfo | undefined {
    return this.anime().sonarrAnimes?.[0];
  }

  get plex() {
    return this.anime().plexAnimes?.[0];
  }

  // ── Seasonal data helpers (prefer seasonalItem when set) ──────────────────

  get inMal(): boolean {
    return this.seasonalItem()?.inMal ?? !!this.mal?.id;
  }

  get inSonarr(): boolean {
    return this.seasonalItem()?.inSonarr ?? !!this.sonarr?.id;
  }

  get inPlex(): boolean {
    return this.seasonalItem()?.inPlex ?? !!this.plex?.id;
  }

  get sonarrUrl(): string | null {
    return this.seasonalItem()?.sonarrUrl ?? this.sonarr?.url ?? null;
  }

  get plexUrl(): string | null {
    return this.seasonalItem()?.plexUrl ?? this.plex?.url ?? null;
  }

  get malUrl(): string | null {
    return this.seasonalItem()?.malUrl ?? this.mal?.url ?? null;
  }

  get malId(): string | null {
    return this.seasonalItem()?.malId ?? this.mal?.id ?? null;
  }

  get tvdbId(): string | undefined {
    return this.seasonalItem()?.tvdbId;
  }

  get englishTitle(): string | undefined {
    return this.seasonalItem()?.englishTitle;
  }

  /** True when at least one provider has the anime tracked. */
  get hasProviderData(): boolean {
    return this.inMal || this.inSonarr || this.inPlex;
  }

  /** True when the anime is tracked in all applicable providers. */
  get allTracked(): boolean {
    const sonarrTracked = !this.sonarrEnabled() || this.inSonarr;
    return sonarrTracked && this.inMal;
  }

  get partiallyTracked(): boolean {
    return this.inSonarr !== this.inMal && (this.inSonarr || this.inMal);
  }

  get sonarrMissing(): boolean {
    return this.partiallyTracked && !this.inSonarr;
  }

  get malMissing(): boolean {
    return this.partiallyTracked && !this.inMal;
  }

  get anyMissing(): boolean {
    return !this.inSonarr || !this.inMal;
  }

  get anythingSelected(): boolean {
    return (!this.inSonarr && this.addSonarr()) || (!this.inMal && this.addMal());
  }

  // ── Title / image ─────────────────────────────────────────────────────────

  get coverImageUrl(): string | undefined {
    return this.seasonalItem()?.coverImageUrl ?? this.mal?.coverImageUrl;
  }

  /** Best-effort localised title based on current UI language */
  get displayTitle(): string {
    const seasonal = this.seasonalItem();
    if (seasonal) return seasonal.title;
    const lang = this.translate.currentLang ?? 'en';
    const alt = this.mal?.alternativeTitles;
    if (alt) {
      if (lang === 'ja' && alt.ja) return alt.ja;
      if (alt.en && alt.en !== this.anime().title) return alt.en;
    }
    return this.anime().title;
  }

  get subtitle(): string | undefined {
    const dt = this.displayTitle;
    if (dt !== this.anime().title) return this.anime().title;
    const ja = this.mal?.alternativeTitles?.ja;
    return ja && ja !== dt ? ja : undefined;
  }

  get allAltTitles(): string[] {
    const alt = this.mal?.alternativeTitles;
    if (!alt) return [];
    const titles: string[] = [];
    if (alt.en) titles.push(alt.en);
    if (alt.ja) titles.push(alt.ja);
    if (alt.synonyms) titles.push(...alt.synonyms);
    return [...new Set(titles)].filter((t) => t !== this.displayTitle);
  }

  // ── Description (CSS-truncated to 3 lines — no "see more" modal) ──────────

  get description(): string {
    return this.seasonalItem()?.synopsis ?? this.mal?.synopsis ?? this.anime().description ?? '';
  }

  // ── Status ────────────────────────────────────────────────────────────────

  get malViewState(): string | undefined {
    return this.mal?.viewState;
  }

  get seriesStatus(): string | undefined {
    return this.seasonalItem()?.status ?? this.mal?.status;
  }

  get mediaType(): string | undefined {
    return this.seasonalItem()?.mediaType ?? this.mal?.mediaType;
  }

  mediaTypeLabel(type: string): string {
    const key = `animeCard.mediaType.${type}`;
    const t = this.translate.instant(key);
    return t !== key ? t : type.toUpperCase();
  }

  viewStateLabel(state: string): string {
    const key = `animeCard.malStatus.${state}`;
    const t = this.translate.instant(key);
    return t !== key ? t : state;
  }

  seriesStatusLabel(status: string): string {
    const key = `animeCard.seriesStatus.${status}`;
    const t = this.translate.instant(key);
    return t !== key ? t : status.replace(/_/g, ' ');
  }

  // ── Genres ────────────────────────────────────────────────────────────────

  private get allMalGenres(): string[] {
    return this.seasonalItem()?.genres ?? this.mal?.genres ?? [];
  }

  get pureGenres(): string[] {
    return this.allMalGenres.filter((g) => GENRE_LIST.includes(g));
  }

  get themes(): string[] {
    return this.allMalGenres.filter((g) => THEME_LIST.includes(g));
  }

  get demographics(): string[] {
    return this.allMalGenres.filter((g) => DEMOGRAPHIC_LIST.includes(g));
  }

  translateGenre(name: string): string {
    const key = `genres.${name}`;
    const t = this.translate.instant(key);
    return t !== key ? t : name;
  }

  // ── Scores ────────────────────────────────────────────────────────────────

  get malScore(): number {
    return this.seasonalItem()?.malScore ?? this.mal?.rating?.malMean ?? 0;
  }

  get myScore(): number {
    return this.mal?.rating?.myScore ?? 0;
  }

  // ── Dates ─────────────────────────────────────────────────────────────────

  get startDate(): string | undefined {
    return this.seasonalItem()?.startDate ?? this.mal?.startDate;
  }

  get endDate(): string | undefined {
    return this.mal?.endDate;
  }

  get addedDate(): Date | undefined {
    const raw = this.mal?.addedDate;
    if (!raw) return undefined;
    const d = new Date(raw);
    return d.getFullYear() > 1 ? d : undefined;
  }

  // ── Interaction ───────────────────────────────────────────────────────────

  toggleAltTitles(): void {
    this.showAltTitles.update((v) => !v);
  }

  /** Opens the detail panel. Dismisses onboarding hint if it was shown. */
  onContentClick(): void {
    if (this.showOnboardingHint()) this.onboardingDismissed.emit();
    const seasonal = this.seasonalItem();
    if (seasonal) {
      if (seasonal.aniDbId) {
        this.openDetails.emit(seasonal.aniDbId);
      } else {
        this.openDetailsByMalId.emit(seasonal.malId);
      }
      return;
    }
    this.openDetails.emit(this.anime().aniDbId);
  }

  openMalPage(): void {
    if (this.malUrl) window.open(this.malUrl, '_blank', 'noopener,noreferrer');
  }

  openPlexLink(event: Event): void {
    const url = this.plexUrl;
    if (!url) return;
    event.preventDefault();
    this.plexDeepLink.openLink(url, this.displayTitle);
  }

  openSynopsisModal(): void {
    this.popupService.openSynopsis(this.displayTitle, this.description);
  }

  openFranchiseConstellation(): void {
    const id = this.malId;
    if (!id) return;
    this.franchiseConstellation.open(id, this.displayTitle, this.coverImageUrl);
  }

  openLightbox(): void {
    if (this.coverImageUrl) this.showLightbox.set(true);
  }

  closeLightbox(): void {
    this.showLightbox.set(false);
  }

  // ── Unified add modal ─────────────────────────────────────────────────────

  openAddModal(): void {
    const seasonal = this.seasonalItem()!;
    // Only pre-select Sonarr if it is both missing AND enabled globally
    this.addSonarr.set(!seasonal.inSonarr && this.sonarrEnabled());
    this.addMal.set(!seasonal.inMal);
    this.addError.set('');
    this.addSuccess.set(false);
    this.lastAddedSonarrId.set(null);
    this.addMalStatus.set('plan_to_watch');
    this.addMalEpisodes.set(0);
    this.addMalScore.set(0);

    // Auto-expand the first missing provider (skip Sonarr if disabled)
    if (!seasonal.inSonarr && this.sonarrEnabled()) {
      this.expandedProvider.set('sonarr');
    } else if (!seasonal.inMal) {
      this.expandedProvider.set('mal');
    } else {
      this.expandedProvider.set(null);
    }

    this.showAddModal.set(true);
    this.popupService.addProviderModalOpen.set(true);

    // Kick off Sonarr search + options when Sonarr is pre-selected and enabled
    if (!seasonal.inSonarr && this.sonarrEnabled()) {
      this.initSonarrSection();
    }
  }

  closeAddModal(): void {
    this.showAddModal.set(false);
    this.popupService.addProviderModalOpen.set(false);
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

  confirmAdd(): void {
    if (this.addBusy() || !this.anythingSelected) return;
    const willAddSonarr = !this.inSonarr && this.addSonarr();
    const willAddMal = !this.inMal && this.addMal();
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
    const seasonal = this.seasonalItem()!;
    if (seasonal.tvdbId) return `tvdb:${seasonal.tvdbId}`;
    if (seasonal.englishTitle?.trim()) return seasonal.englishTitle.trim();
    return seasonal.title;
  }

  /** Initialises the Sonarr search term + results and loads options from scratch. */
  private initSonarrSection(): void {
    const term = this.getSonarrSearchTerm();
    this.sonarrSearchTerm.set(term);
    this.sonarrSelected.set(null);
    this.sonarrSearchResults.set([]);
    this.sonarrSearchError.set('');
    // Emit an empty string first so debounce sees a value change, then the real term.
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

          const folder =
            (defaultRootFolder ? opts.rootFolders.find((f) => f.path === defaultRootFolder) : null) ??
            opts.rootFolders[0];
          if (folder) this.sonarrRootFolder.set(folder.path);

          const profile =
            (defaultProfileName ? opts.qualityProfiles.find((p) => p.name === defaultProfileName) : null) ??
            opts.qualityProfiles[0];
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
    const seasonal = this.seasonalItem()!;
    if (seasonal.inSonarr || !this.addSonarr()) {
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
        malId: seasonal.malId,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result: SonarrAddResult) => {
          this.lastAddedSonarrId.set(result.sonarrId);
          (seasonal as { inSonarr: boolean }).inSonarr = true;
          if (result.sonarrUrl) (seasonal as { sonarrUrl?: string }).sonarrUrl = result.sonarrUrl;
          onSuccess();
        },
        error: (err) => {
          this.addError.set(this._extractErrorMessage(err, 'seasonal.sonarrAddError'));
          this.addBusy.set(false);
        },
      });
  }

  private doAddMal(onSuccess: () => void): void {
    const seasonal = this.seasonalItem()!;
    if (seasonal.inMal || !this.addMal()) {
      onSuccess();
      return;
    }

    this.animeService
      .updateMalListStatus(seasonal.malId, {
        status: this.addMalStatus(),
        numWatchedEpisodes: this.addMalEpisodes(),
        score: this.addMalScore(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          (seasonal as { inMal: boolean }).inMal = true;
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
                  (seasonal as { inSonarr: boolean }).inSonarr = false;
                  (seasonal as { sonarrUrl?: string }).sonarrUrl = undefined;
                  this.lastAddedSonarrId.set(null);
                },
                error: () => {
                  (seasonal as { inSonarr: boolean }).inSonarr = false;
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

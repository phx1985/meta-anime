import { Component, Input, signal, inject, HostListener, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { trigger, state, style, animate, transition } from '@angular/animations';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DestroyRef } from '@angular/core';
import { SeasonalAnimeItem } from '../../models/anime.models';
import { ScoreBadgeComponent } from '../../anime-list/anime-card/score-badge/score-badge.component';
import { SonarrService } from '../../services/sonarr.service';
import { AnimeService } from '../../services/anime.service';
import { SonarrRootFolder, SonarrQualityProfile, SonarrLookupResult, SonarrAddResult } from '../../models/sonarr.models';
import { Subject, debounceTime, distinctUntilChanged, switchMap, of, catchError } from 'rxjs';

@Component({
  selector: 'app-seasonal-card',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslatePipe, ScoreBadgeComponent],
  templateUrl: './seasonal-card.component.html',
  styleUrl: './seasonal-card.component.scss',
  animations: [
    trigger('expandCollapse', [
      state('collapsed', style({ height: '3.75rem', overflow: 'hidden' })),
      state('expanded',  style({ height: '*',       overflow: 'hidden' })),
      transition('collapsed => expanded', animate('380ms cubic-bezier(0.4, 0, 0.2, 1)')),
      transition('expanded => collapsed', animate('280ms cubic-bezier(0.4, 0, 1, 1)')),
    ])
  ]
})
export class SeasonalCardComponent implements OnInit {
  @Input({ required: true }) anime!: SeasonalAnimeItem;

  private translate    = inject(TranslateService);
  private sonarrService = inject(SonarrService);
  private animeService  = inject(AnimeService);
  private destroyRef   = inject(DestroyRef);

  descExpanded  = signal(false);
  showLightbox  = signal(false);

  // ── Unified add modal ────────────────────────────────────────────────────
  showAddModal        = signal(false);
  addBusy             = signal(false);
  addError            = signal('');
  addSuccess          = signal(false);
  addSonarr           = signal(false);   // user selected to add to Sonarr
  addMal              = signal(false);   // user selected to add to MAL
  lastAddedSonarrId   = signal<number | null>(null); // for rollback

  // ── Sonarr add state ─────────────────────────────────────────────────────
  sonarrRootFolders     = signal<SonarrRootFolder[]>([]);
  sonarrQualityProfiles = signal<SonarrQualityProfile[]>([]);
  sonarrLoadingOptions  = signal(false);
  sonarrSearchTerm      = signal('');
  sonarrSearchResults   = signal<SonarrLookupResult[]>([]);
  sonarrSearching       = signal(false);
  sonarrSearchError     = signal('');
  sonarrSelected        = signal<SonarrLookupResult | null>(null);
  sonarrRootFolder      = signal('');
  sonarrProfileId       = signal(0);

  private sonarrSearch$ = new Subject<string>();

  // ── MAL add state ─────────────────────────────────────────────────────────
  malStatus     = signal('plan_to_watch');
  malEpisodes   = signal(0);
  malScore      = signal(0);

  readonly malStatuses = [
    { value: 'watching',      labelKey: 'seasonal.malStatus.watching' },
    { value: 'completed',     labelKey: 'seasonal.malStatus.completed' },
    { value: 'on_hold',       labelKey: 'seasonal.malStatus.on_hold' },
    { value: 'dropped',       labelKey: 'seasonal.malStatus.dropped' },
    { value: 'plan_to_watch', labelKey: 'seasonal.malStatus.plan_to_watch' },
  ];

  ngOnInit(): void {
    this.sonarrSearch$
      .pipe(
        debounceTime(350),
        distinctUntilChanged(),
        switchMap(term => {
          if (!term.trim()) return of([]);
          this.sonarrSearching.set(true);
          this.sonarrSearchError.set('');
          return this.sonarrService.lookup(term).pipe(
            catchError(() => {
              this.sonarrSearchError.set(this.translate.instant('seasonal.sonarrSearchError'));
              return of([]);
            })
          );
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(results => {
        this.sonarrSearchResults.set(results);
        this.sonarrSearching.set(false);
      });
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.showLightbox()) { this.showLightbox.set(false); return; }
    if (this.showAddModal()) { this.closeAddModal(); }
  }

  get coverImageUrl(): string | undefined { return this.anime.coverImageUrl; }
  get displayTitle(): string { return this.anime.title; }
  get description(): string { return this.anime.synopsis ?? ''; }
  get genres(): string[] { return this.anime.genres ?? []; }
  get displayMalScore(): number { return this.anime.malScore ?? 0; }
  get startDate(): string | undefined { return this.anime.startDate; }
  get mediaType(): string | undefined { return this.anime.mediaType; }
  get seriesStatus(): string | undefined { return this.anime.status; }
  get malUrl(): string | null { return this.anime.malUrl ?? null; }

  /** True when the anime is tracked in both Sonarr and MAL. */
  get allTracked(): boolean { return this.anime.inSonarr && this.anime.inMal; }

  /** True when at least one writable provider does not have this anime. */
  get anyMissing(): boolean { return !this.anime.inSonarr || !this.anime.inMal; }

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

  toggleDesc(): void { this.descExpanded.update(v => !v); }
  openLightbox(): void { if (this.coverImageUrl) this.showLightbox.set(true); }
  closeLightbox(): void { this.showLightbox.set(false); }

  // ── Unified add modal ─────────────────────────────────────────────────────

  openAddModal(): void {
    // Pre-select missing providers
    this.addSonarr.set(!this.anime.inSonarr);
    this.addMal.set(!this.anime.inMal);
    this.addError.set('');
    this.addSuccess.set(false);
    this.lastAddedSonarrId.set(null);
    // Reset MAL fields
    this.malStatus.set('plan_to_watch');
    this.malEpisodes.set(0);
    this.malScore.set(0);
    this.showAddModal.set(true);

    // If Sonarr is pre-selected, auto-search + load options
    if (!this.anime.inSonarr) {
      this.sonarrSearchTerm.set(this.anime.title);
      this.sonarrSelected.set(null);
      this.sonarrSearchResults.set([]);
      this.sonarrSearchError.set('');
      this.sonarrSearch$.next(this.anime.title);

      if (this.sonarrRootFolders().length === 0) {
        this.sonarrLoadingOptions.set(true);
        this.sonarrService.getOptions()
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: opts => {
              this.sonarrRootFolders.set(opts.rootFolders);
              this.sonarrQualityProfiles.set(opts.qualityProfiles);
              if (opts.rootFolders.length > 0) this.sonarrRootFolder.set(opts.rootFolders[0].path);
              if (opts.qualityProfiles.length > 0) this.sonarrProfileId.set(opts.qualityProfiles[0].id);
              this.sonarrLoadingOptions.set(false);
            },
            error: () => {
              this.addError.set(this.translate.instant('seasonal.sonarrOptionsError'));
              this.sonarrLoadingOptions.set(false);
            }
          });
      }
    }
  }

  closeAddModal(): void { this.showAddModal.set(false); }

  toggleProvider(provider: 'sonarr' | 'mal'): void {
    if (provider === 'sonarr') {
      const next = !this.addSonarr();
      this.addSonarr.set(next);
      // Trigger search when enabling Sonarr section
      if (next && this.sonarrSearchResults().length === 0) {
        this.sonarrSearchTerm.set(this.anime.title);
        this.sonarrSearch$.next(this.anime.title);
        if (this.sonarrRootFolders().length === 0) {
          this.sonarrLoadingOptions.set(true);
          this.sonarrService.getOptions()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              next: opts => {
                this.sonarrRootFolders.set(opts.rootFolders);
                this.sonarrQualityProfiles.set(opts.qualityProfiles);
                if (opts.rootFolders.length > 0) this.sonarrRootFolder.set(opts.rootFolders[0].path);
                if (opts.qualityProfiles.length > 0) this.sonarrProfileId.set(opts.qualityProfiles[0].id);
                this.sonarrLoadingOptions.set(false);
              },
              error: () => this.sonarrLoadingOptions.set(false)
            });
        }
      }
    } else {
      this.addMal.set(!this.addMal());
    }
  }

  onSonarrSearchInput(term: string): void {
    this.sonarrSearchTerm.set(term);
    this.sonarrSelected.set(null);
    this.sonarrSearch$.next(term);
  }

  selectSonarrResult(item: SonarrLookupResult): void { this.sonarrSelected.set(item); }

  get anythingSelected(): boolean {
    return (!this.anime.inSonarr && this.addSonarr()) || (!this.anime.inMal && this.addMal());
  }

  confirmAdd(): void {
    if (this.addBusy() || !this.anythingSelected) return;
    this.addBusy.set(true);
    this.addError.set('');
    this.doAddSonarr(() => {
      this.doAddMal(() => {
        this.addSuccess.set(true);
        this.addBusy.set(false);
        setTimeout(() => this.closeAddModal(), 1500);
      });
    });
  }

  private doAddSonarr(onSuccess: () => void): void {
    if (this.anime.inSonarr || !this.addSonarr()) { onSuccess(); return; }

    const selected = this.sonarrSelected();
    if (!selected) {
      this.addError.set(this.translate.instant('seasonal.sonarrNoneSelected'));
      this.addBusy.set(false);
      return;
    }

    this.sonarrService.addSeries({
      tvdbId: selected.tvdbId,
      rootFolderPath: this.sonarrRootFolder(),
      qualityProfileId: this.sonarrProfileId(),
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
        this.addError.set(err?.error ?? this.translate.instant('seasonal.sonarrAddError'));
        this.addBusy.set(false);
      }
    });
  }

  private doAddMal(onSuccess: () => void): void {
    if (this.anime.inMal || !this.addMal()) { onSuccess(); return; }

    this.animeService.updateMalListStatus(this.anime.malId, {
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
        // Rollback Sonarr if it was just added in this same session
        const sonarrId = this.lastAddedSonarrId();
        if (sonarrId !== null) {
          this.sonarrService.removeSeries(sonarrId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
              next: () => {
                (this.anime as { inSonarr: boolean }).inSonarr = false;
                (this.anime as { sonarrUrl?: string }).sonarrUrl = undefined;
                this.lastAddedSonarrId.set(null);
              },
              // If rollback also fails, the UI state flag is reset but the series
              // may remain in Sonarr — append a note so the user is aware.
              error: () => {
                (this.anime as { inSonarr: boolean }).inSonarr = false;
                this.lastAddedSonarrId.set(null);
              }
            });
        }
        this.addError.set(err?.error ?? this.translate.instant('seasonal.malAddError'));
        this.addBusy.set(false);
      }
    });
  }
}

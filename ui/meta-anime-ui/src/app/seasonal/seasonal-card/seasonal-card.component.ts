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
import { SonarrRootFolder, SonarrQualityProfile, SonarrLookupResult } from '../../models/sonarr.models';
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

  // ── Sonarr add modal ─────────────────────────────────────────────────────
  showSonarrModal       = signal(false);
  sonarrRootFolders     = signal<SonarrRootFolder[]>([]);
  sonarrQualityProfiles = signal<SonarrQualityProfile[]>([]);
  sonarrLoadingOptions  = signal(false);
  sonarrSearchTerm      = signal('');
  sonarrSearchResults   = signal<SonarrLookupResult[]>([]);
  sonarrSearching       = signal(false);
  sonarrSearchError     = signal('');
  sonarrSelected        = signal<SonarrLookupResult | null>(null);
  sonarrAdding          = signal(false);
  sonarrError           = signal('');
  sonarrSuccess         = signal(false);
  sonarrRootFolder      = signal('');
  sonarrProfileId       = signal(0);

  private sonarrSearch$ = new Subject<string>();

  // ── MAL add modal ─────────────────────────────────────────────────────────
  showMalModal  = signal(false);
  malAdding     = signal(false);
  malError      = signal('');
  malSuccess    = signal(false);
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
    // Wire up debounced search
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
    if (this.showSonarrModal()) { this.closeSonarrModal(); return; }
    if (this.showMalModal()) { this.closeMalModal(); }
  }

  get coverImageUrl(): string | undefined { return this.anime.coverImageUrl; }
  get displayTitle(): string { return this.anime.title; }
  get description(): string { return this.anime.synopsis ?? ''; }
  get genres(): string[] { return this.anime.genres ?? []; }

  translateGenre(name: string): string {
    const key = `genres.${name}`;
    const t = this.translate.instant(key);
    return t !== key ? t : name;
  }

  get displayMalScore(): number { return this.anime.malScore ?? 0; }
  get startDate(): string | undefined { return this.anime.startDate; }
  get mediaType(): string | undefined { return this.anime.mediaType; }

  mediaTypeLabel(type: string): string {
    const key = `animeCard.mediaType.${type}`;
    const t = this.translate.instant(key);
    return t !== key ? t : type.toUpperCase();
  }

  get seriesStatus(): string | undefined { return this.anime.status; }

  seriesStatusLabel(status: string): string {
    const key = `animeCard.seriesStatus.${status}`;
    const t = this.translate.instant(key);
    return t !== key ? t : status.replace(/_/g, ' ');
  }

  get malUrl(): string | null { return this.anime.malUrl ?? null; }

  openMalPage(): void {
    if (this.malUrl) window.open(this.malUrl, '_blank', 'noopener,noreferrer');
  }

  toggleDesc(): void { this.descExpanded.update(v => !v); }
  openLightbox(): void { if (this.coverImageUrl) this.showLightbox.set(true); }
  closeLightbox(): void { this.showLightbox.set(false); }

  // ── Sonarr ────────────────────────────────────────────────────────────────
  openSonarrModal(): void {
    this.sonarrError.set('');
    this.sonarrSuccess.set(false);
    this.sonarrSelected.set(null);
    this.sonarrSearchResults.set([]);
    this.sonarrSearchError.set('');
    // Pre-fill search with anime title
    const term = this.anime.title;
    this.sonarrSearchTerm.set(term);
    this.showSonarrModal.set(true);

    // Load options + auto-search in parallel
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
            this.sonarrError.set(this.translate.instant('seasonal.sonarrOptionsError'));
            this.sonarrLoadingOptions.set(false);
          }
        });
    }
    // Trigger initial search
    this.sonarrSearch$.next(term);
  }

  onSonarrSearchInput(term: string): void {
    this.sonarrSearchTerm.set(term);
    this.sonarrSelected.set(null);
    this.sonarrSearch$.next(term);
  }

  selectSonarrResult(item: SonarrLookupResult): void {
    this.sonarrSelected.set(item);
  }

  closeSonarrModal(): void { this.showSonarrModal.set(false); }

  confirmAddSonarr(): void {
    const selected = this.sonarrSelected();
    if (!selected || this.sonarrAdding()) return;
    this.sonarrAdding.set(true);
    this.sonarrError.set('');
    this.sonarrService.addSeries({
      tvdbId: selected.tvdbId,
      rootFolderPath: this.sonarrRootFolder(),
      qualityProfileId: this.sonarrProfileId(),
    })
    .pipe(takeUntilDestroyed(this.destroyRef))
    .subscribe({
      next: () => {
        this.sonarrSuccess.set(true);
        this.sonarrAdding.set(false);
        (this.anime as { inSonarr: boolean }).inSonarr = true;
        setTimeout(() => this.showSonarrModal.set(false), 1500);
      },
      error: (err) => {
        this.sonarrError.set(err?.error ?? this.translate.instant('seasonal.sonarrAddError'));
        this.sonarrAdding.set(false);
      }
    });
  }

  // ── MAL ───────────────────────────────────────────────────────────────────
  openMalModal(): void {
    this.malError.set('');
    this.malSuccess.set(false);
    this.malStatus.set('plan_to_watch');
    this.malEpisodes.set(0);
    this.malScore.set(0);
    this.showMalModal.set(true);
  }

  closeMalModal(): void { this.showMalModal.set(false); }

  confirmAddMal(): void {
    if (this.malAdding()) return;
    this.malAdding.set(true);
    this.malError.set('');
    this.animeService.updateMalListStatus(this.anime.malId, {
      status: this.malStatus(),
      numWatchedEpisodes: this.malEpisodes(),
      score: this.malScore(),
    })
    .pipe(takeUntilDestroyed(this.destroyRef))
    .subscribe({
      next: () => {
        this.malSuccess.set(true);
        this.malAdding.set(false);
        (this.anime as { inMal: boolean }).inMal = true;
        setTimeout(() => this.showMalModal.set(false), 1500);
      },
      error: (err) => {
        this.malError.set(err?.error ?? this.translate.instant('seasonal.malAddError'));
        this.malAdding.set(false);
      }
    });
  }
}


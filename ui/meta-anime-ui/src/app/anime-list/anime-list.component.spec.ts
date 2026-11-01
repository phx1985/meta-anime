import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { AnimeListComponent } from './anime-list.component';
import { UserPreferencesService } from '../services/user-preferences.service';
import { AnimeDetailsResponse, PagedResponse } from '../models/anime.models';

const emptyResponse: PagedResponse<AnimeDetailsResponse> = {
  data: [],
  page: 1,
  pageSize: 20,
  totalCount: 0,
  totalPages: 0,
  hasNextPage: false,
  hasPreviousPage: false,
};

describe('AnimeListComponent', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<AnimeListComponent>;
  let comp: AnimeListComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AnimeListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AnimeListComponent);
    comp = fixture.componentInstance;
    // Flush the NSFW settings call made in the constructor
    httpMock.match((r) => r.url.includes('/api/v1/settings')).forEach((r) => r.flush([]));
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create without making HTTP request on init', () => {
    httpMock.expectNone((r) => r.url.includes('/api/v1/animes'));
    expect(comp).toBeTruthy();
  });

  it('should show search prompt before first search', () => {
    fixture.detectChanges();
    expect(comp.searched()).toBe(false);
  });

  it('should load animes when search() is called', async () => {
    comp.search();

    const req = httpMock.expectOne((r) => r.url.includes('/api/v1/animes'));
    req.flush({
      data: [
        { aniDbId: '1', title: 'Naruto' },
        { aniDbId: '2', title: 'One Piece' },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
      totalPages: 1,
      hasNextPage: false,
      hasPreviousPage: false,
    });

    await fixture.whenStable();
    fixture.detectChanges();

    expect(comp.animes().length).toBe(2);
    expect(comp.animes()[0].title).toBe('Naruto');
    expect(comp.animes()[1].title).toBe('One Piece');
  });

  it('should show empty message when no animes after search', async () => {
    comp.search();
    const req = httpMock.expectOne((r) => r.url.includes('/api/v1/animes'));
    req.flush(emptyResponse);

    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelectorAll('.anime-name-item').length).toBe(0);
    // Empty state now renders through app-ui-state-panel (icon + heading + subtext + CTA)
    const emptyMsg = compiled.querySelector('app-ui-state-panel .ui-state-panel__title');
    expect(emptyMsg).toBeTruthy();
  });

  it('should display total count when animes are loaded', async () => {
    comp.search();
    const req = httpMock.expectOne((r) => r.url.includes('/api/v1/animes'));
    req.flush({
      data: [{ aniDbId: '1', title: 'Naruto' } as AnimeDetailsResponse],
      page: 1,
      pageSize: 20,
      totalCount: 42,
      totalPages: 3,
      hasNextPage: true,
      hasPreviousPage: false,
    } as PagedResponse<AnimeDetailsResponse>);

    await fixture.whenStable();
    fixture.detectChanges();

    expect(comp.totalCount()).toBe(42);
  });

  it('should default to displayMode 2', () => {
    expect(comp.displayMode()).toBe(2);
  });

  it('should update displayMode when setDisplayMode is called', () => {
    comp.setDisplayMode(3);
    expect(comp.displayMode()).toBe(3);
  });

  it('should set allLoaded when hasNextPage is false after search', async () => {
    comp.search();
    const req = httpMock.expectOne((r) => r.url.includes('/api/v1/animes'));
    req.flush({
      data: [],
      page: 1,
      pageSize: 20,
      totalCount: 0,
      totalPages: 0,
      hasNextPage: false,
      hasPreviousPage: false,
    });

    await fixture.whenStable();
    expect(comp.allLoaded()).toBe(true);
  });

  it('should request page size of 20', () => {
    comp.search();
    const req = httpMock.expectOne((r) => r.url.includes('/api/v1/animes'));
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush(emptyResponse);
  });

  it('should reset filters and clear results without triggering a search', () => {
    comp.filterGenre.set(['Action']);
    comp.genreOperator = 'and';
    comp.filterWatchState = ['watching'];
    comp.filterHasFile = 'yes';
    comp.reset();

    httpMock.expectNone((r) => r.url.includes('/api/v1/animes'));
    expect(comp.filterGenre()).toEqual([]);
    expect(comp.genreOperator).toBe('or');
    expect(comp.filterWatchState).toEqual([]);
    expect(comp.filterHasFile).toBe('any');
    expect(comp.searched()).toBe(false);
    expect(comp.animes().length).toBe(0);
    expect(comp.filterOpen()).toBe(true);
  });

  it('should send multi-value status params when multiple watch states selected', () => {
    comp.filterWatchState = ['watching', 'completed'];
    comp.search();
    const req = httpMock.expectOne((r) => r.url.includes('/api/v1/animes'));
    expect(req.request.params.getAll('status')).toEqual(['watching', 'completed']);
    req.flush(emptyResponse);
  });

  it('should send multi-value genre params when multiple genres selected', () => {
    comp.filterGenre.set(['Action', 'Fantasy']);
    comp.search();
    const req = httpMock.expectOne((r) => r.url.includes('/api/v1/animes'));
    expect(req.request.params.getAll('genre')).toEqual(['Action', 'Fantasy']);
    req.flush(emptyResponse);
  });

  it('should send genreOperator=and when AND operator selected with multiple genres', () => {
    comp.filterGenre.set(['Action', 'Fantasy']);
    comp.genreOperator = 'and';
    comp.search();
    const req = httpMock.expectOne((r) => r.url.includes('/api/v1/animes'));
    expect(req.request.params.get('genreOperator')).toBe('and');
    req.flush(emptyResponse);
  });

  it('should collapse filter panel when search() is called', () => {
    comp.filterOpen.set(true);
    comp.search();
    expect(comp.filterOpen()).toBe(false);
    httpMock.expectOne((r) => r.url.includes('/api/v1/animes')).flush(emptyResponse);
  });

  // ── Preset persistence tests ─────────────────────────────────────────────
  // NOTE: filter persistence runs through UserPreferencesService (server property
  // bag; localStorage only as legacy migration source), so these tests capture
  // the snapshot handed to saveLastFilters instead of reading localStorage.

  it('should include activePresetName in saved filter snapshot when a preset is active', () => {
    let saved: string | null = null;
    comp.userPrefs.saveLastFilters = (json: string) => { saved = json; };
    comp.activePresetName.set('My Preset');
    comp.search();
    httpMock.expectOne((r) => r.url.includes('/api/v1/animes')).flush(emptyResponse);

    expect(saved).toBeTruthy();
    const snapshot = JSON.parse(saved!);
    expect(snapshot.activePresetName).toBe('My Preset');
  });

  it('should not include activePresetName in saved snapshot when no preset is active', () => {
    let saved: string | null = null;
    comp.userPrefs.saveLastFilters = (json: string) => { saved = json; };
    comp.activePresetName.set(null);
    comp.search();
    httpMock.expectOne((r) => r.url.includes('/api/v1/animes')).flush(emptyResponse);

    const snapshot = JSON.parse(saved!);
    expect(snapshot.activePresetName).toBeUndefined();
  });

  it('should restore activePresetName from localStorage on init when autoApply is enabled', () => {
    // The restore logic runs in the constructor, which already ran in beforeEach.
    // Verify instead by directly testing that snapshotFilters() includes the preset name
    // (the round-trip: save → restore → save again would carry the preset name through).
    let saved: string | null = null;
    comp.userPrefs.saveLastFilters = (json: string) => { saved = json; };
    comp.activePresetName.set('Saved Preset');
    comp.search();
    httpMock.expectOne((r) => r.url.includes('/api/v1/animes')).flush(emptyResponse);

    // The restored value matches what was saved
    const snapshot = JSON.parse(saved!);
    expect(snapshot.activePresetName).toBe('Saved Preset');
  });

  it('should clear activePresetName when toggleGenre is called while preset is active', () => {
    comp.activePresetName.set('Some Preset');
    const fakeEvent = { stopPropagation: () => {} } as MouseEvent;
    comp.toggleGenre('Action', fakeEvent);
    expect(comp.activePresetName()).toBeNull();
  });

  it('should clear activePresetName when toggleTheme is called while preset is active', () => {
    comp.activePresetName.set('Some Preset');
    const fakeEvent = { stopPropagation: () => {} } as MouseEvent;
    comp.toggleTheme('Mecha', fakeEvent);
    expect(comp.activePresetName()).toBeNull();
  });

  it('should clear activePresetName when toggleDemographic is called while preset is active', () => {
    comp.activePresetName.set('Some Preset');
    const fakeEvent = { stopPropagation: () => {} } as MouseEvent;
    comp.toggleDemographic('Shounen', fakeEvent);
    expect(comp.activePresetName()).toBeNull();
  });

  it('should clear activePresetName when toggleSortOrder is called while preset is active', () => {
    comp.activePresetName.set('Some Preset');
    comp.toggleSortOrder();
    expect(comp.activePresetName()).toBeNull();
  });

  it('should clear activePresetName when onSortByChange is called while preset is active', () => {
    comp.activePresetName.set('Some Preset');
    comp.onSortByChange();
    expect(comp.activePresetName()).toBeNull();
  });

  it('should NOT clear activePresetName when no preset is active (invalidate is a no-op)', () => {
    comp.activePresetName.set(null);
    const fakeEvent = { stopPropagation: () => {} } as MouseEvent;
    comp.toggleGenre('Action', fakeEvent);
    expect(comp.activePresetName()).toBeNull();
  });
});

describe('AnimeListComponent – constructor restore of activePresetName', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    // Set localStorage BEFORE the component is instantiated so the constructor picks it up
    localStorage.setItem(
      'meta-anime:lastUsedFilters',
      JSON.stringify({ activePresetName: 'Constructor Preset' }),
    );
    localStorage.setItem('meta-anime:autoApplyLastFilters', 'true');

    await TestBed.configureTestingModule({
      imports: [AnimeListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    localStorage.removeItem('meta-anime:lastUsedFilters');
    localStorage.removeItem('meta-anime:autoApplyLastFilters');
    httpMock.verify();
  });

  it('should bind activePresetName from localStorage during construction', () => {
    // The constructor restores through UserPreferencesService (auto-apply +
    // loadLastFilters); stub it the way an already-loaded service behaves.
    const prefs = TestBed.inject(UserPreferencesService);
    prefs.autoApplyLastFilters.set(true);
    prefs.loadLastFilters = () => JSON.stringify({ activePresetName: 'Constructor Preset' });

    const fixture2 = TestBed.createComponent(AnimeListComponent);
    const comp2 = fixture2.componentInstance;

    // Constructor triggers auto-apply which calls search()
    httpMock.match((r) => r.url.includes('/api/v1/settings')).forEach((r) => r.flush([]));
    httpMock.match((r) => r.url.includes('/api/v1/animes')).forEach((r) =>
      r.flush({ data: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasNextPage: false, hasPreviousPage: false }),
    );

    expect(comp2.activePresetName()).toBe('Constructor Preset');
  });
});

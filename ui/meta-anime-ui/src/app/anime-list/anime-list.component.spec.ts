import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { AnimeListComponent } from './anime-list.component';
import { AnimeDetailsResponse, PagedResponse } from '../models/anime.models';

const emptyResponse: PagedResponse<AnimeDetailsResponse> = {
  data: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasNextPage: false, hasPreviousPage: false,
};

describe('AnimeListComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AnimeListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(AnimeListComponent);
    const req = httpMock.expectOne(r => r.url.includes('/api/v1/animes'));
    req.flush(emptyResponse);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should display anime names', async () => {
    const fixture = TestBed.createComponent(AnimeListComponent);
    const req = httpMock.expectOne(r => r.url.includes('/api/v1/animes'));
    req.flush({ data: [
      { aniDbId: '1', title: 'Naruto' },
      { aniDbId: '2', title: 'One Piece' },
    ], page: 1, pageSize: 20, totalCount: 2, totalPages: 1, hasNextPage: false, hasPreviousPage: false });

    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.animes().length).toBe(2);
    expect(fixture.componentInstance.animes()[0].title).toBe('Naruto');
    expect(fixture.componentInstance.animes()[1].title).toBe('One Piece');
  });

  it('should show empty message when no animes', async () => {
    const fixture = TestBed.createComponent(AnimeListComponent);
    const req = httpMock.expectOne(r => r.url.includes('/api/v1/animes'));
    req.flush(emptyResponse);

    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelectorAll('.anime-name-item').length).toBe(0);
    const emptyMsg = compiled.querySelector('p.text-muted');
    expect(emptyMsg).toBeTruthy();
  });

  it('should display total count when animes are loaded', async () => {
    const fixture = TestBed.createComponent(AnimeListComponent);
    const req = httpMock.expectOne(r => r.url.includes('/api/v1/animes'));
    req.flush({ data: [{ aniDbId: '1', title: 'Naruto' } as AnimeDetailsResponse],
      page: 1, pageSize: 20, totalCount: 42, totalPages: 3, hasNextPage: true, hasPreviousPage: false } as PagedResponse<AnimeDetailsResponse>);

    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.totalCount()).toBe(42);
  });

  it('should default to displayMode 2', () => {
    const fixture = TestBed.createComponent(AnimeListComponent);
    httpMock.expectOne(r => r.url.includes('/api/v1/animes')).flush(emptyResponse);
    expect(fixture.componentInstance.displayMode()).toBe(2);
  });

  it('should update displayMode when setDisplayMode is called', () => {
    const fixture = TestBed.createComponent(AnimeListComponent);
    httpMock.expectOne(r => r.url.includes('/api/v1/animes')).flush(emptyResponse);
    fixture.componentInstance.setDisplayMode(3);
    expect(fixture.componentInstance.displayMode()).toBe(3);
  });

  it('should set allLoaded when hasNextPage is false', async () => {
    const fixture = TestBed.createComponent(AnimeListComponent);
    const req = httpMock.expectOne(r => r.url.includes('/api/v1/animes'));
    req.flush({ data: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasNextPage: false, hasPreviousPage: false });

    await fixture.whenStable();
    expect(fixture.componentInstance.allLoaded()).toBe(true);
  });

  it('should request page size of 20', () => {
    TestBed.createComponent(AnimeListComponent);
    const req = httpMock.expectOne(r => r.url.includes('/api/v1/animes'));
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush(emptyResponse);
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { AnimeListComponent } from './anime-list.component';
import { AnimeResponse, AnimeSource, PagedResponse } from '../models/anime.models';

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
    req.flush({ data: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0, hasNextPage: false, hasPreviousPage: false } as PagedResponse<AnimeResponse>);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should display anime names', async () => {
    const fixture = TestBed.createComponent(AnimeListComponent);
    const mockAnimes: AnimeResponse[] = [
      { id: '1', sourceId: 's1', title: 'Naruto', animeSource: AnimeSource.Plex },
      { id: '2', sourceId: 's2', title: 'One Piece', animeSource: AnimeSource.MyAnimeList },
    ];
    const req = httpMock.expectOne(r => r.url.includes('/api/v1/animes'));
    req.flush({ data: mockAnimes, page: 1, pageSize: 100, totalCount: 2, totalPages: 1, hasNextPage: false, hasPreviousPage: false } as PagedResponse<AnimeResponse>);

    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const items = compiled.querySelectorAll('.anime-name-item');
    expect(items.length).toBe(2);
    expect(items[0].textContent?.trim()).toBe('Naruto');
    expect(items[1].textContent?.trim()).toBe('One Piece');
  });

  it('should show empty message when no animes', async () => {
    const fixture = TestBed.createComponent(AnimeListComponent);
    const req = httpMock.expectOne(r => r.url.includes('/api/v1/animes'));
    req.flush({ data: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0, hasNextPage: false, hasPreviousPage: false } as PagedResponse<AnimeResponse>);

    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelectorAll('.anime-name-item').length).toBe(0);
    const emptyMsg = compiled.querySelector('p.text-muted');
    expect(emptyMsg).toBeTruthy();
  });
});

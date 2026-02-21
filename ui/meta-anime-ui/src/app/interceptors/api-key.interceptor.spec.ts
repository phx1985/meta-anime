import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { HttpClient } from '@angular/common/http';
import { apiKeyInterceptor } from './api-key.interceptor';
import * as env from '../../environments/environment';

describe('apiKeyInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should attach X-Api-Key header to outgoing requests when apiKey is set', () => {
    const originalKey = env.environment.apiKey;
    (env.environment as { apiKey: string }).apiKey = 'test-key';

    http.get('/api/v1/animes').subscribe();

    const req = httpMock.expectOne('/api/v1/animes');
    expect(req.request.headers.has('X-Api-Key')).toBeTruthy();
    req.flush([]);

    (env.environment as { apiKey: string }).apiKey = originalKey;
  });

  it('should not attach X-Api-Key header when apiKey is empty', () => {
    const originalKey = env.environment.apiKey;
    (env.environment as { apiKey: string }).apiKey = '';

    http.get('/api/v1/animes').subscribe();

    const req = httpMock.expectOne('/api/v1/animes');
    expect(req.request.headers.has('X-Api-Key')).toBeFalsy();
    req.flush([]);

    (env.environment as { apiKey: string }).apiKey = originalKey;
  });
});

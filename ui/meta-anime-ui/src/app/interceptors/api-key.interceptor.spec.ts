import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { HttpClient } from '@angular/common/http';
import { apiKeyInterceptor } from './api-key.interceptor';
import { RuntimeConfigService } from '../services/runtime-config.service';

describe('apiKeyInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let runtimeConfigService: RuntimeConfigService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    runtimeConfigService = TestBed.inject(RuntimeConfigService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should attach X-Api-Key header to outgoing requests when apiKey is set', () => {
    runtimeConfigService.setApiKeyForTesting('test-key');

    http.get('/api/v1/animes').subscribe();

    const req = httpMock.expectOne('/api/v1/animes');
    expect(req.request.headers.has('X-Api-Key')).toBeTruthy();
    expect(req.request.headers.get('X-Api-Key')).toBe('test-key');
    req.flush([]);
  });

  it('should not attach X-Api-Key header when apiKey is empty', () => {
    runtimeConfigService.setApiKeyForTesting('');

    http.get('/api/v1/animes').subscribe();

    const req = httpMock.expectOne('/api/v1/animes');
    expect(req.request.headers.has('X-Api-Key')).toBeFalsy();
    req.flush([]);
  });
});

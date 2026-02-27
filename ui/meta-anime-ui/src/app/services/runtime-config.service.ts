import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

interface RuntimeConfig {
  apiKey: string;
}

@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  private http = inject(HttpClient);
  private config: RuntimeConfig = { apiKey: environment.apiKey };

  async load(): Promise<void> {
    try {
      const loaded = await firstValueFrom(
        this.http.get<RuntimeConfig>('/assets/config.json'),
      );
      // config.json takes precedence when it provides a non-empty key;
      // otherwise the build-time environment default is kept.
      if (loaded.apiKey) {
        this.config = loaded;
      }
    } catch (err) {
      console.warn('[RuntimeConfigService] Could not load /assets/config.json; using environment default.', err);
    }
  }

  get apiKey(): string {
    return this.config.apiKey;
  }

  /** For testing only: set the in-memory API key without making an HTTP request. */
  setApiKeyForTesting(key: string): void {
    this.config = { apiKey: key };
  }
}

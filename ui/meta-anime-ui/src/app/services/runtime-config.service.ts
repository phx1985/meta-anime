import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

interface RuntimeConfig {
  apiKey: string;
}

@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  private http = inject(HttpClient);
  private config: RuntimeConfig = { apiKey: '' };

  async load(): Promise<void> {
    try {
      this.config = await firstValueFrom(
        this.http.get<RuntimeConfig>('/assets/config.json'),
      );
    } catch (err) {
      console.warn('[RuntimeConfigService] Could not load /assets/config.json; API key will be empty.', err);
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

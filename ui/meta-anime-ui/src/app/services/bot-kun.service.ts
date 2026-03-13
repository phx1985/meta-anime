import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthService } from './auth.service';

export type BotKunRole = 'user' | 'assistant';

export interface BotKunHistoryMessage {
  role: BotKunRole;
  content: string;
}

@Injectable({ providedIn: 'root' })
export class BotKunService {
  private readonly authService = inject(AuthService);
  private readonly http = inject(HttpClient);
  private abortController: AbortController | null = null;
  private greetingAbortController: AbortController | null = null;

  sendMessage(userMessage: string, history: BotKunHistoryMessage[]): Observable<string> {
    return new Observable<string>(subscriber => {
      this.abortController = new AbortController();
      const { signal } = this.abortController;

      const token = this.authService.getToken();
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      if (token) headers['Authorization'] = `Bearer ${token}`;

      fetch('/api/v1/bot-kun/chat', {
        method: 'POST',
        headers,
        body: JSON.stringify({ userMessage, history }),
        signal,
      })
        .then(async response => {
          if (!response.ok || !response.body) {
            subscriber.error(new Error(`HTTP ${response.status}`));
            return;
          }
          await this.consumeSseStream(response, subscriber);
        })
        .catch(err => {
          if ((err as Error).name === 'AbortError') {
            subscriber.complete();
          } else {
            subscriber.error(err);
          }
        });

      return () => this.abortController?.abort();
    });
  }

  streamGreeting(): Observable<string> {
    return new Observable<string>(subscriber => {
      this.greetingAbortController?.abort();
      this.greetingAbortController = new AbortController();
      const { signal } = this.greetingAbortController;

      const token = this.authService.getToken();
      const headers: Record<string, string> = {};
      if (token) headers['Authorization'] = `Bearer ${token}`;

      fetch('/api/v1/bot-kun/greeting', {
        method: 'GET',
        headers,
        signal,
      })
        .then(async response => {
          if (!response.ok || !response.body) {
            subscriber.error(new Error(`HTTP ${response.status}`));
            return;
          }
          await this.consumeSseStream(response, subscriber);
        })
        .catch(err => {
          if ((err as Error).name === 'AbortError') {
            subscriber.complete();
          } else {
            subscriber.error(err);
          }
        });

      return () => this.greetingAbortController?.abort();
    });
  }

  cancel(): void {
    this.abortController?.abort();
    this.abortController = null;
  }

  cancelGreeting(): void {
    this.greetingAbortController?.abort();
    this.greetingAbortController = null;
  }

  fetchModels(baseUrl: string, apiKey: string): Observable<string[]> {
    return this.http.post<string[]>('/api/v1/bot-kun/models', { baseUrl, apiKey });
  }

  private async consumeSseStream(
    response: Response,
    subscriber: { next(v: string): void; complete(): void; error(e: unknown): void },
  ): Promise<void> {
    const reader = response.body!.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() ?? '';

        for (const line of lines) {
          if (!line.startsWith('data: ')) continue;
          const data = line.slice(6).trim();
          if (data === '[DONE]') {
            subscriber.complete();
            return;
          }
          try {
            const parsed = JSON.parse(data) as { c?: string };
            if (parsed.c) subscriber.next(parsed.c);
          } catch {
            // malformed chunk — skip
          }
        }
      }
    } finally {
      reader.releaseLock();
    }

    subscriber.complete();
  }
}



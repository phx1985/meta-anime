import { Injectable, inject, signal } from '@angular/core';
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

  // R17b: permitted MAL IDs emitted by the backend before the LLM stream.
  // Non-empty only during DB-path responses; used by the component to validate [anime:ID] tags.
  readonly permittedMalIds = signal<ReadonlySet<string>>(new Set());

  /** Prompt queued by the anime detail panel's "Ask Bot-kun" button. */
  private readonly _pendingPrompt = signal<string | null>(null);
  readonly pendingPrompt = this._pendingPrompt.asReadonly();

  requestPrompt(text: string): void {
    this._pendingPrompt.set(text);
  }

  consumePrompt(): void {
    this._pendingPrompt.set(null);
  }

  sendMessage(
    userMessage: string,
    history: BotKunHistoryMessage[],
    language = 'en',
    previousSearchPage?: number,
    previousSearchQuery?: string,
  ): Observable<string> {
    return new Observable<string>(subscriber => {
      this.abortController = new AbortController();
      const { signal } = this.abortController;

      // R17b: reset permitted IDs at the start of every new message
      this.permittedMalIds.set(new Set());

      const token = this.authService.getToken();
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      if (token) headers['Authorization'] = `Bearer ${token}`;

      const body: Record<string, unknown> = { userMessage, history, language };
      if (previousSearchPage !== undefined) body['previousSearchPage'] = previousSearchPage;
      if (previousSearchQuery)              body['previousSearchQuery'] = previousSearchQuery;

      fetch('/api/v1/bot-kun/chat', {
        method: 'POST',
        headers,
        body: JSON.stringify(body),
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
        let readResult: ReadableStreamReadResult<Uint8Array>;
        try {
          readResult = await reader.read();
        } catch (readErr) {
          // S1+S2: catch network-level read errors and forward to subscriber
          subscriber.error(readErr);
          return;
        }
        const { done, value } = readResult;
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
            if (parsed.c) {
              // R17b: intercept PI metadata chunk (permitted MAL IDs); don't forward as content
              if (parsed.c.startsWith('[PI:') && parsed.c.endsWith(']')) {
                const ids = parsed.c.slice(4, -1).split(',').filter(Boolean);
                if (ids.length > 0) this.permittedMalIds.set(new Set(ids));
                continue;
              }
              subscriber.next(parsed.c);
            }
          } catch {
            // S4: log malformed chunks instead of silent skip
            console.warn('[Bot-kun] SSE chunk parse failed:', data.slice(0, 100));
          }
        }
      }
    } finally {
      reader.releaseLock();
    }

    subscriber.complete();
  }
}


import { Injectable, signal, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AnimeService } from './anime.service';
import { FranchiseConstellationData } from '../models/anime.models';

@Injectable({ providedIn: 'root' })
export class FranchiseConstellationService {
  private animeService = inject(AnimeService);

  /** Whether the constellation overlay is visible. */
  readonly isOpen = signal(false);
  /** True while the MAL API request is in flight. */
  readonly loading = signal(false);
  /** Non-null when the request failed. */
  readonly error = signal<string | null>(null);
  /** The franchise data once loaded. */
  readonly data = signal<FranchiseConstellationData | null>(null);

  /** Session-scoped in-memory cache — bypasses repeated API calls within the same session. */
  private readonly cache = new Map<string, FranchiseConstellationData>();

  /**
   * Opens the constellation overlay for the given MAL anime ID.
   * Uses cached data when available; otherwise fires one MAL API call.
   */
  open(malId: string, title: string, coverImageUrl?: string): void {
    this.error.set(null);
    this.data.set(null);
    this.isOpen.set(true);

    if (this.cache.has(malId)) {
      this.data.set(this.cache.get(malId)!);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.animeService.getFranchiseConstellation(malId).subscribe({
      next: (result) => {
        // Merge cover/title from local data when MAL doesn't return them
        const data: FranchiseConstellationData = {
          malId: result.malId ?? malId,
          title: result.title || title,
          coverImageUrl: result.coverImageUrl ?? coverImageUrl,
          relations: result.relations ?? [],
        };
        this.cache.set(malId, data);
        this.data.set(data);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(this._extractErrorMessage(err));
        this.loading.set(false);
      },
    });
  }

  close(): void {
    this.isOpen.set(false);
  }

  private _extractErrorMessage(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      if (typeof err.error === 'string' && err.error.length > 0) return err.error;
      if (err.error && typeof err.error === 'object') {
        const obj = err.error as Record<string, unknown>;
        const msg = obj['message'] ?? obj['title'] ?? obj['errorMessage'];
        if (typeof msg === 'string') return msg;
      }
      return err.message || 'Failed to load franchise data';
    }
    if (err instanceof Error) return err.message;
    return 'Failed to load franchise data';
  }
}

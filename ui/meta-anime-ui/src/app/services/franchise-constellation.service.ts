import { Injectable, signal, inject } from '@angular/core';
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
      error: (err) => {
        const msg =
          (err?.error as string | undefined) ??
          (err?.message as string | undefined) ??
          'Failed to load franchise data';
        this.error.set(msg);
        this.loading.set(false);
      },
    });
  }

  close(): void {
    this.isOpen.set(false);
  }
}

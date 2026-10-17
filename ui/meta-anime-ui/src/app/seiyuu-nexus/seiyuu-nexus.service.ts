import { Injectable, inject, signal } from '@angular/core';
import { SeiyuuNexusResponse, VoiceActorProfileResponse } from '../models/anime.models';
import { AnimeService } from '../services/anime.service';

@Injectable({ providedIn: 'root' })
export class SeiyuuNexusService {
  private readonly animeService = inject(AnimeService);

  readonly isOpen = signal(false);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly nexus = signal<SeiyuuNexusResponse | null>(null);
  readonly zIndexBase = signal(1099);

  readonly focusedVa = signal<VoiceActorProfileResponse | null>(null);
  readonly vaLoading = signal(false);

  private readonly cache = new Map<string, SeiyuuNexusResponse>();
  private readonly vaCache = new Map<number, VoiceActorProfileResponse>();

  open(malId: string, callerZIndex = 1099): void {
    this.zIndexBase.set(callerZIndex);
    this.error.set(null);
    this.focusedVa.set(null);
    this.isOpen.set(true);

    if (this.cache.has(malId)) {
      this.nexus.set(this.cache.get(malId)!);
      this.loading.set(false);
      return;
    }

    this.nexus.set(null);
    this.loading.set(true);

    this.animeService.getSeiyuuNexus(malId).subscribe({
      next: data => {
        this.cache.set(malId, data);
        this.nexus.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('error');
        this.loading.set(false);
      },
    });
  }

  close(): void {
    this.isOpen.set(false);
  }

  focusVa(personId: number): void {
    if (this.vaCache.has(personId)) {
      this.focusedVa.set(this.vaCache.get(personId)!);
      return;
    }

    this.vaLoading.set(true);
    this.animeService.getVoiceActorProfile(personId).subscribe({
      next: profile => {
        this.vaCache.set(personId, profile);
        this.focusedVa.set(profile);
        this.vaLoading.set(false);
      },
      error: () => {
        this.vaLoading.set(false);
      },
    });
  }
}

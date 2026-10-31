import { Injectable, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { SeiyuuNexusResponse, VoiceActorProfileResponse } from '../models/anime.models';
import { AnimeService } from '../services/anime.service';

/** Lifecycle of the focused voice-actor profile inside the inspector. */
export type VaProfileStatus = 'idle' | 'loading' | 'ready' | 'notFound' | 'error';

/**
 * State store for the Seiyuu Nexus overlay.
 *
 * Stale-response guards: every fetch carries the id it was issued for and the
 * response is dropped when the UI has already moved on (`pendingAnimeMalId`,
 * `pendingVaPersonId`). Caches are keyed by anime MAL id / person id and a
 * cache hit always clears any in-flight loading state.
 */
@Injectable({ providedIn: 'root' })
export class SeiyuuNexusService {
  private readonly animeService = inject(AnimeService);

  /** Overlay visibility. */
  readonly isOpen = signal(false);
  /**
   * Caller z-index (kept for `open(malId, zIndex)` API stability). Unused for
   * stacking now that the overlay is a top-layer <dialog> — the top layer sits
   * above every z-index in the document by construction.
   */
  readonly zIndexBase = signal(1099);
  /**
   * Id of the anime currently shown — the persistence key for the language
   * filter. (Spec name `activeRequestMalId` covers this + `pendingAnimeMalId`
   * below: one signal per role kept for clarity.)
   */
  readonly animeMalId = signal<string | null>(null);

  readonly nexus = signal<SeiyuuNexusResponse | null>(null);
  /** First load (or post-open load) in flight — drives the skeleton state. */
  readonly loading = signal(false);
  /** Last load failed (503 / network) — drives the distinct error state. */
  readonly error = signal(false);
  /** Retry request in flight — the error state stays up with an icon-only spin. */
  readonly retrying = signal(false);

  readonly focusedVaPersonId = signal<number | null>(null);
  readonly vaProfile = signal<VoiceActorProfileResponse | null>(null);
  readonly vaProfileStatus = signal<VaProfileStatus>('idle');

  private readonly cache = new Map<string, SeiyuuNexusResponse>();
  private readonly vaCache = new Map<number, VoiceActorProfileResponse>();
  /** Anime the UI is waiting on — drops stale, out-of-order nexus responses. */
  private pendingAnimeMalId: string | null = null;
  /** Person the UI is waiting on — drops stale, out-of-order profile responses. */
  private pendingVaPersonId: number | null = null;

  open(malId: string, callerZIndex = 1099): void {
    this.zIndexBase.set(callerZIndex);
    this.isOpen.set(true);
    if (this.animeMalId() !== malId) {
      this.animeMalId.set(malId);
      this.closeProfile();
    }
    this.error.set(false);
    this.fetchNexus(malId, false);
  }

  close(): void {
    this.isOpen.set(false);
  }

  /**
   * Re-fetch after a failed load. Never stacks requests: while a load or a
   * retry is already in flight the call is dropped so the backend pacing is
   * respected (no hammering a provider that is already down).
   */
  retry(): void {
    const malId = this.animeMalId();
    if (!malId || this.loading() || this.retrying()) return;
    this.cache.delete(malId);
    this.fetchNexus(malId, true);
  }

  private fetchNexus(malId: string, retryAttempt: boolean): void {
    const cached = this.cache.get(malId);
    if (cached) {
      // Cache hit must always clear any in-flight loading/retrying state.
      this.pendingAnimeMalId = null;
      this.nexus.set(cached);
      this.loading.set(false);
      this.retrying.set(false);
      return;
    }

    this.pendingAnimeMalId = malId;
    if (retryAttempt) {
      this.retrying.set(true);
    } else {
      this.nexus.set(null);
      this.loading.set(true);
    }

    this.animeService.getSeiyuuNexus(malId).subscribe({
      next: data => {
        this.cache.set(malId, data);
        // Anime switch guard: rapid X->Y drops X's response entirely.
        if (this.pendingAnimeMalId !== malId) return;
        this.nexus.set(data);
        this.loading.set(false);
        this.retrying.set(false);
        this.error.set(false);
      },
      error: () => {
        if (this.pendingAnimeMalId !== malId) return;
        this.loading.set(false);
        this.retrying.set(false);
        this.error.set(true);
      },
    });
  }

  focusVa(personId: number): void {
    this.focusedVaPersonId.set(personId);

    const cached = this.vaCache.get(personId);
    if (cached) {
      this.pendingVaPersonId = null;
      this.vaProfile.set(cached);
      this.vaProfileStatus.set('ready');
      return;
    }

    this.pendingVaPersonId = personId;
    this.vaProfile.set(null);
    this.vaProfileStatus.set('loading');

    this.animeService.getVoiceActorProfile(personId).subscribe({
      next: profile => {
        this.vaCache.set(personId, profile);
        // VA click guard: A->B never renders A's bio under B.
        if (this.pendingVaPersonId !== personId) return;
        this.vaProfile.set(profile);
        this.vaProfileStatus.set('ready');
      },
      error: (err: HttpErrorResponse) => {
        if (this.pendingVaPersonId !== personId) return;
        this.vaProfileStatus.set(err.status === 404 ? 'notFound' : 'error');
      },
    });
  }

  closeProfile(): void {
    this.pendingVaPersonId = null;
    this.focusedVaPersonId.set(null);
    this.vaProfile.set(null);
    this.vaProfileStatus.set('idle');
  }
}

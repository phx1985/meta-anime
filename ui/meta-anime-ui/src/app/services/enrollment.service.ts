import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface MalAuthStatusResponse {
  connected: boolean;
}

export interface MalAuthInitiateResponse {
  authorizationUrl: string;
}

export interface PlexAuthStatusResponse {
  connected: boolean;
}

export interface PlexAuthInitiateResponse {
  pinId: number;
  authUrl: string;
}

export interface PlexAuthPollResponse {
  connected: boolean;
}

@Injectable({ providedIn: 'root' })
export class EnrollmentService {
  private http = inject(HttpClient);
  private malBase = '/api/v1/mal/auth';
  private plexBase = '/api/v1/plex/auth';

  // ── MAL ─────────────────────────────────────────────────────────────────

  getMalStatus(): Observable<MalAuthStatusResponse> {
    return this.http.get<MalAuthStatusResponse>(`${this.malBase}/status`);
  }

  initiateMalAuth(): Observable<MalAuthInitiateResponse> {
    return this.http.get<MalAuthInitiateResponse>(`${this.malBase}/initiate`);
  }

  disconnectMal(): Observable<void> {
    return this.http.delete<void>(`${this.malBase}/disconnect`);
  }

  // ── Plex ─────────────────────────────────────────────────────────────────

  getPlexStatus(): Observable<PlexAuthStatusResponse> {
    return this.http.get<PlexAuthStatusResponse>(`${this.plexBase}/status`);
  }

  initiatePlexPin(): Observable<PlexAuthInitiateResponse> {
    return this.http.post<PlexAuthInitiateResponse>(`${this.plexBase}/initiate`, null);
  }

  pollPlexPin(pinId: number): Observable<PlexAuthPollResponse> {
    return this.http.post<PlexAuthPollResponse>(`${this.plexBase}/poll/${pinId}`, null);
  }

  disconnectPlex(): Observable<void> {
    return this.http.delete<void>(`${this.plexBase}/disconnect`);
  }
}

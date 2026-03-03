import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

export interface AuthStatusResponse {
  requiresAuth: boolean;
}

export interface LoginResponse {
  token: string;
}

const JWT_KEY = 'meta_anime_jwt';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private base = '/api/v1/auth';

  /** Reactive signal — true when a JWT is stored. Updated by login/logout/setupPassword. */
  private readonly _authenticated = signal<boolean>(localStorage.getItem(JWT_KEY) !== null);

  /** Returns whether admin password protection is enabled on the backend. */
  getStatus(): Observable<AuthStatusResponse> {
    return this.http.get<AuthStatusResponse>(`${this.base}/status`);
  }

  /** Authenticates with the admin password and stores the returned JWT. */
  login(password: string): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.base}/login`, { password })
      .pipe(tap((res) => { localStorage.setItem(JWT_KEY, res.token); this._authenticated.set(true); }));
  }

  /** Sends the setup-password request and stores the returned JWT. */
  setupPassword(password: string): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.base}/setup-password`, { password })
      .pipe(tap((res) => { localStorage.setItem(JWT_KEY, res.token); this._authenticated.set(true); }));
  }

  /** Changes the admin password. Requires valid auth. */
  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.base}/change-password`, { currentPassword, newPassword });
  }

  /** Disables password protection. Requires valid auth and current password confirmation. */
  disableAuth(currentPassword: string): Observable<void> {
    return this.http.post<void>(`${this.base}/disable-auth`, { currentPassword });
  }

  /** Clears the stored JWT (logout). */
  logout(): void {
    localStorage.removeItem(JWT_KEY);
    this._authenticated.set(false);
  }

  /** Returns the stored JWT or null. */
  getToken(): string | null {
    return localStorage.getItem(JWT_KEY);
  }

  /** Reactive: true when a JWT is stored. Updates instantly on login/logout across all components. */
  isAuthenticated(): boolean {
    return this._authenticated();
  }
}

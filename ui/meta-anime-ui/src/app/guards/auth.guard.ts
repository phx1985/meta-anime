import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { map, catchError } from 'rxjs/operators';
import { of } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Route guard that checks whether admin password protection is active.
 * If the backend requires auth and the user has no JWT, redirects to /login.
 */
export const authGuard = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  return authService.getStatus().pipe(
    map((status) => {
      if (!status.requiresAuth) return true;
      if (authService.isAuthenticated()) return true;
      return router.createUrlTree(['/login']);
    }),
    catchError(() => of(true)), // On network error, allow access (fail-open)
  );
};

import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { targetsOwnApi } from './request-target.util';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const token = authService.getToken();
  // Only attach the JWT to our own backend — never to cross-origin requests.
  const authReq = token && targetsOwnApi(req.url)
    ? req.clone({ headers: req.headers.set('Authorization', `Bearer ${token}`) })
    : req;

  return next(authReq).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401) {
        // Clear the stale token and redirect to login.
        authService.logout();
        router.navigate(['/login']);
      }
      return throwError(() => err);
    }),
  );
};

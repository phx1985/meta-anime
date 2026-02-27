import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { NotificationService } from '../services/notification.service';

const MAL_AUTH_ERROR_CODE = 'MAL.AuthenticationFailed';

function isMalAuthError(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse)) return false;
  if (error.status === 401) {
    const body = error.error;
    if (body && typeof body === 'object' && 'code' in body) {
      return body['code'] === MAL_AUTH_ERROR_CODE;
    }
  }
  return false;
}

export const malAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const notifService = inject(NotificationService);
  return next(req).pipe(
    catchError((error: unknown) => {
      if (isMalAuthError(error)) {
        notifService.push({
          id: `mal-auth-${Date.now()}`,
          severity: 'error',
          titleKey: 'notifications.malAuth.title',
          messageKey: 'notifications.malAuth.message',
          action: {
            labelKey: 'notifications.malAuth.action',
            route: ['/settings'],
            fragment: 'section-mal',
          },
        });
      }
      return throwError(() => error);
    }),
  );
};

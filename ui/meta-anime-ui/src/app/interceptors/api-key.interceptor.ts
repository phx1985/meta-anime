import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../environments/environment';

export const apiKeyInterceptor: HttpInterceptorFn = (req, next) => {
  if (!environment.apiKey) {
    return next(req);
  }

  const authReq = req.clone({
    headers: req.headers.set('X-Api-Key', environment.apiKey)
  });

  return next(authReq);
};

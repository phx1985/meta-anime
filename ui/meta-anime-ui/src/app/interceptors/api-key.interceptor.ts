import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { ApiConstants } from '../constants/api.constants';

export const apiKeyInterceptor: HttpInterceptorFn = (req, next) => {
  if (!environment.apiKey) {
    return next(req);
  }

  const authReq = req.clone({
    headers: req.headers.set(ApiConstants.apiKeyHeader, environment.apiKey),
  });

  return next(authReq);
};

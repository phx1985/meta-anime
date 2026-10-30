import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { ApiConstants } from '../constants/api.constants';
import { RuntimeConfigService } from '../services/runtime-config.service';
import { targetsOwnApi } from './request-target.util';

export const apiKeyInterceptor: HttpInterceptorFn = (req, next) => {
  const apiKey = inject(RuntimeConfigService).apiKey;

  // Only attach the API key to our own backend — never to cross-origin requests.
  if (!apiKey || !targetsOwnApi(req.url)) {
    return next(req);
  }

  const authReq = req.clone({
    headers: req.headers.set(ApiConstants.apiKeyHeader, apiKey),
  });

  return next(authReq);
};

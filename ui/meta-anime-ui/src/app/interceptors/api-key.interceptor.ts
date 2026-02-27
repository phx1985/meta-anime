import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { ApiConstants } from '../constants/api.constants';
import { RuntimeConfigService } from '../services/runtime-config.service';

export const apiKeyInterceptor: HttpInterceptorFn = (req, next) => {
  const apiKey = inject(RuntimeConfigService).apiKey;

  if (!apiKey) {
    return next(req);
  }

  const authReq = req.clone({
    headers: req.headers.set(ApiConstants.apiKeyHeader, apiKey),
  });

  return next(authReq);
};

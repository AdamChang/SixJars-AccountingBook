import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { NEVER, catchError, throwError } from 'rxjs';
import { BrowserLocation } from '../browser-location';
import { classifyError } from './api-error';
import { ANTIFORGERY_MESSAGE, UNAVAILABLE_MESSAGE } from './messages';
import { Notifier } from './notifier';

export const apiErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const location = inject(BrowserLocation);
  const notifier = inject(Notifier);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      const apiError = classifyError(error);
      switch (apiError.kind) {
        case 'unauthorized':
          location.assign('/auth/login?returnUrl=' + encodeURIComponent(location.currentPath()));
          // 整頁即將離開，不讓畫面再處理這個錯誤
          return NEVER;
        case 'antiforgery':
          notifier.show(ANTIFORGERY_MESSAGE);
          break;
        case 'unavailable':
          notifier.show(UNAVAILABLE_MESSAGE);
          break;
      }
      return throwError(() => apiError);
    }),
  );
};

import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';

import { AuthService, SKIP_AUTH } from './auth.service';

const withToken = (request: HttpRequest<unknown>, token: string | null) =>
  token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

/**
 * Adds the access token to API requests. If the API still answers 401 (e.g. the token was revoked or the
 * user's roles changed), refreshes once and retries; if that fails, the user has been signed out.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/') || request.context.get(SKIP_AUTH)) {
    return next(request);
  }

  const auth = inject(AuthService);
  return auth.getAccessToken().pipe(
    switchMap((token) =>
      next(withToken(request, token)).pipe(
        catchError((error: unknown) => {
          if (!token || !(error instanceof HttpErrorResponse) || error.status !== 401) {
            return throwError(() => error);
          }
          return auth
            .refresh()
            .pipe(
              switchMap((fresh) =>
                fresh ? next(withToken(request, fresh)) : throwError(() => error),
              ),
            );
        }),
      ),
    ),
  );
};

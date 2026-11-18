import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, throwError } from 'rxjs';

/**
 * Network failures and server errors are reported once, here; callers still receive the error.
 * 4xx responses are left to the calling page (validation, sign-in, conflicts).
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const snackBar = inject(MatSnackBar);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && (error.status === 0 || error.status >= 500)) {
        const message =
          error.status === 0
            ? 'Немає зʼєднання з сервером. Перевірте інтернет і спробуйте ще раз.'
            : 'На сервері сталася помилка. Спробуйте пізніше.';
        snackBar.open(message, 'Закрити', { duration: 6000 });
      }
      return throwError(() => error);
    }),
  );
};

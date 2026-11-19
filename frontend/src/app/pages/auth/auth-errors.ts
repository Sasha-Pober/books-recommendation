import { HttpErrorResponse } from '@angular/common/http';

/**
 * A message for a failed sign-in or registration. Network and server errors are already reported by the
 * error interceptor, so they get a short generic line here.
 */
export function authErrorMessage(error: unknown, kind: 'login' | 'register'): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Щось пішло не так. Спробуйте ще раз.';
  }
  switch (error.status) {
    case 401:
      return 'Невірний email або пароль.';
    case 409:
      return 'Акаунт із цим email уже існує. Увійдіть або скористайтеся іншою адресою.';
    case 400:
      return kind === 'register'
        ? 'Перевірте введені дані: email має бути дійсним, пароль — відповідати вимогам.'
        : 'Перевірте введені дані.';
    case 429:
      return 'Забагато спроб. Зачекайте хвилину й спробуйте знову.';
    default:
      return 'Не вдалося виконати запит. Спробуйте пізніше.';
  }
}

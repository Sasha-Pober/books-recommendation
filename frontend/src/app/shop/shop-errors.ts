import { HttpErrorResponse } from '@angular/common/http';

/** A message for a failed cart/wishlist/order/rating action (network and 5xx are reported globally). */
export function shopErrorMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Щось пішло не так. Спробуйте ще раз.';
  }
  switch (error.status) {
    case 0:
    case 500:
    case 502:
    case 503:
    case 504:
      return 'Не вдалося виконати дію. Спробуйте ще раз.';
    case 401:
    case 403:
      return 'Увійдіть як покупець, щоб зробити це.';
    case 404:
      return 'Цієї книжки вже немає в каталозі.';
    case 409:
      return 'Такої кількості немає в наявності. Кошик оновлено — перевірте його.';
    case 400:
      return 'Некоректна кількість.';
    default:
      return 'Не вдалося виконати дію. Спробуйте пізніше.';
  }
}

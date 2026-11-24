import { HttpErrorResponse } from '@angular/common/http';

export type AdminAction = 'save' | 'delete' | 'create' | 'activate';

/** A message for a failed management action (network and 5xx errors are also reported globally). */
export function adminErrorMessage(error: unknown, action: AdminAction): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Щось пішло не так. Спробуйте ще раз.';
  }
  switch (error.status) {
    case 401:
    case 403:
      return 'Недостатньо прав. Увійдіть під обліковим записом менеджера або адміністратора.';
    case 404:
      return action === 'activate'
        ? 'Цієї версії моделі вже немає.'
        : 'Цієї книги вже немає в каталозі.';
    case 409:
      return action === 'delete'
        ? 'Книгу вже замовляли, тому її не можна видалити. Щоб зняти її з продажу, встановіть кількість 0.'
        : 'Такий запис уже існує.';
    case 400:
      return 'Перевірте введені дані.';
    case 503:
      return action === 'activate'
        ? 'Рекомендаційний сервіс недоступний або не зміг завантажити модель. Активна версія не змінилась.'
        : 'Сервіс тимчасово недоступний. Спробуйте пізніше.';
    default:
      return 'Не вдалося виконати дію. Спробуйте пізніше.';
  }
}

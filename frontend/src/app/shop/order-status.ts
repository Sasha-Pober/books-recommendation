import type { OrderStatus } from '../api/models';

export const ORDER_STATUS_LABELS: Record<OrderStatus, string> = {
  Pending: 'Нове',
  Paid: 'Оплачене',
  Shipped: 'Відправлене',
  Delivered: 'Доставлене',
  Cancelled: 'Скасоване',
};

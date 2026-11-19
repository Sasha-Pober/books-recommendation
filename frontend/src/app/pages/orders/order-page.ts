import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import type { OrderDto } from '../../api/models';
import { BookLine } from '../../shared/book-line/book-line';
import { ORDER_STATUS_LABELS } from '../../shop/order-status';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

@Component({
  selector: 'app-order-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    BookLine,
    CurrencyPipe,
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    RouterLink,
  ],
  template: `
    <nav class="back">
      <a mat-button routerLink="/orders"><mat-icon>arrow_back</mat-icon> Мої замовлення</a>
    </nav>

    @if (justPlaced()) {
      <div class="placed" role="status">
        <mat-icon>check_circle</mat-icon>
        <div>
          <strong>Дякуємо! Замовлення оформлено.</strong>
          <p>Ми повідомимо, коли його буде відправлено.</p>
        </div>
      </div>
    }

    @if (notFound()) {
      <p class="message">Замовлення не знайдено.</p>
    } @else if (order.error() && !order.isLoading()) {
      <div class="message" role="alert">
        <p>Не вдалося завантажити замовлення.</p>
        <button mat-flat-button (click)="order.reload()">Спробувати ще раз</button>
      </div>
    } @else if (current(); as o) {
      <header>
        <h1>Замовлення від {{ o.createdAt | date: 'd MMMM y, HH:mm' }}</h1>
        <span class="status">{{ statusLabels[o.status] }}</span>
      </header>
      <ul>
        @for (item of o.items; track item.book.id) {
          <li>
            <app-book-line [book]="item.book" [price]="item.unitPrice">
              <span class="quantity">× {{ item.quantity }}</span>
              <span class="line-total">{{
                item.lineTotal | currency: 'UAH' : 'symbol-narrow' : '1.0-2'
              }}</span>
            </app-book-line>
          </li>
        }
      </ul>
      <p class="total">
        Разом: <strong>{{ o.total | currency: 'UAH' : 'symbol-narrow' : '1.0-2' }}</strong>
      </p>
      <p class="number">Номер замовлення: {{ o.id }}</p>
    } @else {
      <mat-progress-bar mode="indeterminate" aria-label="Завантаження" />
    }
  `,
  styles: `
    .back {
      margin: -8px 0 8px -8px;
    }

    .placed {
      display: flex;
      gap: 12px;
      align-items: flex-start;
      margin-bottom: 24px;
      padding: 16px;
      border-radius: 12px;
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);

      p {
        margin: 4px 0 0;
      }
    }

    header {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 8px 16px;

      h1 {
        margin: 0;
        font: var(--mat-sys-headline-small);
      }
    }

    .status {
      padding: 2px 10px;
      border-radius: 12px;
      font: var(--mat-sys-label-medium);
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }

    ul {
      list-style: none;
      margin: 16px 0 0;
      padding: 0;
      max-width: 860px;

      li + li {
        border-top: 1px solid var(--mat-sys-outline-variant);
      }
    }

    .quantity {
      color: var(--mat-sys-on-surface-variant);
    }

    .line-total {
      min-width: 88px;
      text-align: right;
      font: var(--mat-sys-title-small);
    }

    .total {
      max-width: 860px;
      text-align: right;
      font: var(--mat-sys-title-medium);
    }

    .number {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }

    .message {
      padding: 48px 16px;
      text-align: center;
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class OrderPage {
  protected readonly statusLabels = ORDER_STATUS_LABELS;

  /** The :id route parameter. */
  readonly id = input.required<string>();

  protected readonly order = httpResource<OrderDto>(() =>
    GUID.test(this.id()) ? `/api/orders/${this.id()}` : undefined,
  );
  protected readonly current = computed(() =>
    this.order.hasValue() ? this.order.value() : undefined,
  );
  protected readonly notFound = computed(() => {
    const error = this.order.error();
    return !GUID.test(this.id()) || (error instanceof HttpErrorResponse && error.status === 404);
  });
  /** Arrived here straight from checkout (navigation state, so a reload doesn't repeat the thanks). */
  protected readonly justPlaced = computed(
    () =>
      this.current() !== undefined &&
      (history.state as { placed?: boolean } | null)?.placed === true,
  );
}

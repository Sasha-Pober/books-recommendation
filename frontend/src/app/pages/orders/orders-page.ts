import { CurrencyPipe, DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorIntl, MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';

import type { PagedOrders } from '../../api/models';
import { UkrainianPaginatorIntl } from '../../core/paginator-intl';
import { plural } from '../../shared/plural';
import { ORDER_STATUS_LABELS } from '../../shop/order-status';

const PAGE_SIZE = 10;

@Component({
  selector: 'app-orders-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CurrencyPipe,
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    RouterLink,
  ],
  providers: [{ provide: MatPaginatorIntl, useClass: UkrainianPaginatorIntl }],
  template: `
    <h1>Мої замовлення</h1>
    @if (orders.isLoading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Завантаження" />
    }
    @if (orders.error() && !orders.isLoading()) {
      <div class="empty" role="alert">
        <p>Не вдалося завантажити замовлення.</p>
        <button mat-flat-button (click)="orders.reload()">Спробувати ще раз</button>
      </div>
    } @else if (page(); as result) {
      @if (result.items.length) {
        <ul>
          @for (order of result.items; track order.id) {
            <li>
              <a [routerLink]="['/orders', order.id]">
                <span class="date">{{ order.createdAt | date: 'd MMMM y, HH:mm' }}</span>
                <span class="status" [attr.data-status]="order.status">{{
                  statusLabels[order.status]
                }}</span>
                <span class="count">{{ itemsLabel(order.items) }}</span>
                <span class="total">{{
                  order.total | currency: 'UAH' : 'symbol-narrow' : '1.0-2'
                }}</span>
                <mat-icon aria-hidden="true">chevron_right</mat-icon>
              </a>
            </li>
          }
        </ul>
        @if (result.totalCount > pageSize) {
          <mat-paginator
            [length]="result.totalCount"
            [pageIndex]="pageIndex()"
            [pageSize]="pageSize"
            [hidePageSize]="true"
            (page)="changePage($event)"
          />
        }
      } @else {
        <div class="empty">
          <mat-icon>receipt_long</mat-icon>
          <p>Ви ще нічого не замовляли.</p>
          <a mat-flat-button routerLink="/catalog">До каталогу</a>
        </div>
      }
    }
  `,
  styles: `
    h1 {
      margin: 0 0 16px;
      font: var(--mat-sys-headline-small);
    }

    ul {
      list-style: none;
      margin: 0;
      padding: 0;
      max-width: 860px;
    }

    li + li {
      border-top: 1px solid var(--mat-sys-outline-variant);
    }

    a {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto auto 110px 24px;
      gap: 16px;
      align-items: center;
      padding: 16px 8px;
      color: inherit;
      text-decoration: none;
      border-radius: 8px;

      &:hover {
        background: var(--mat-sys-surface-container);
      }
    }

    .status {
      padding: 2px 10px;
      border-radius: 12px;
      font: var(--mat-sys-label-medium);
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);

      &[data-status='Cancelled'] {
        background: var(--mat-sys-error-container);
        color: var(--mat-sys-on-error-container);
      }
    }

    .count {
      color: var(--mat-sys-on-surface-variant);
    }

    .total {
      text-align: right;
      font: var(--mat-sys-title-small);
    }

    mat-paginator {
      max-width: 860px;
      background: transparent;
    }

    .empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      padding: 64px 16px;
      color: var(--mat-sys-on-surface-variant);

      mat-icon {
        font-size: 48px;
        width: 48px;
        height: 48px;
      }

      p {
        margin: 0 0 8px;
        font: var(--mat-sys-title-medium);
      }
    }

    @media (max-width: 600px) {
      a {
        grid-template-columns: minmax(0, 1fr) auto;
        gap: 4px 12px;
      }

      .count,
      mat-icon {
        display: none;
      }
    }
  `,
})
export class OrdersPage {
  protected readonly statusLabels = ORDER_STATUS_LABELS;
  protected readonly pageSize = PAGE_SIZE;
  protected readonly pageIndex = signal(0);

  protected readonly orders = httpResource<PagedOrders>(() => ({
    url: '/api/orders',
    params: { page: this.pageIndex() + 1, pageSize: PAGE_SIZE },
  }));
  protected readonly page = computed(() =>
    this.orders.hasValue() ? this.orders.value() : undefined,
  );

  protected itemsLabel(items: readonly { quantity: number }[]): string {
    const count = items.reduce((sum, i) => sum + i.quantity, 0);
    return `${count} ${plural(count, ['книга', 'книги', 'книг'])}`;
  }

  protected changePage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    window.scrollTo({ top: 0 });
  }
}

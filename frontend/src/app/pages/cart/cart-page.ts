import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';

import type { CartItemDto } from '../../api/models';
import { BookLine } from '../../shared/book-line/book-line';
import { plural } from '../../shared/plural';
import { CartStore } from '../../shop/cart.store';
import { shopErrorMessage } from '../../shop/shop-errors';

/** Max copies of one book per order (CartLimits.MaxQuantity on the server). */
const MAX_QUANTITY = 99;

@Component({
  selector: 'app-cart-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    BookLine,
    CurrencyPipe,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    RouterLink,
  ],
  templateUrl: './cart-page.html',
  styleUrl: './cart-page.scss',
})
export class CartPage {
  private readonly router = inject(Router);
  protected readonly store = inject(CartStore);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly countLabel = computed(() => {
    const count = this.store.count();
    return `${count} ${plural(count, ['книга', 'книги', 'книг'])}`;
  });
  /** A book in the cart is short of stock: checkout would fail. */
  protected readonly shortOfStock = computed(() =>
    (this.store.cart()?.items ?? []).some((i) => i.quantity > i.book.stock),
  );

  constructor() {
    // Fresh prices and stock whenever the cart page opens.
    this.store.reload().subscribe({ error: () => undefined });
  }

  protected maxQuantity(item: CartItemDto): number {
    return Math.max(1, Math.min(MAX_QUANTITY, item.book.stock));
  }

  protected setQuantity(item: CartItemDto, quantity: number): void {
    if (quantity < 1) {
      this.remove(item);
      return;
    }
    this.run(this.store.setQuantity(item.book.id, Math.min(quantity, this.maxQuantity(item))));
  }

  protected remove(item: CartItemDto): void {
    this.run(this.store.remove(item.book.id));
  }

  protected clear(): void {
    this.run(this.store.clear());
  }

  protected checkout(): void {
    this.busy.set(true);
    this.error.set(null);
    this.store.checkout().subscribe({
      next: (order) =>
        void this.router.navigate(['/orders', order.id], { state: { placed: true } }),
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(shopErrorMessage(error));
        this.store.reload().subscribe({ error: () => undefined });
      },
    });
  }

  private run(request: ReturnType<CartStore['reload']>): void {
    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: () => this.busy.set(false),
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(shopErrorMessage(error));
        this.store.reload().subscribe({ error: () => undefined });
      },
    });
  }
}

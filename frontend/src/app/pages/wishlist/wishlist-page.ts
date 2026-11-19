import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import type { WishlistItemDto } from '../../api/models';
import { BookLine } from '../../shared/book-line/book-line';
import { CartStore } from '../../shop/cart.store';
import { shopErrorMessage } from '../../shop/shop-errors';
import { WishlistStore } from '../../shop/wishlist.store';

@Component({
  selector: 'app-wishlist-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BookLine, MatButtonModule, MatIconModule, RouterLink],
  template: `
    <h1>Список бажань</h1>
    @if (store.loaded()) {
      @if (store.items().length) {
        <ul>
          @for (item of store.items(); track item.book.id) {
            <li>
              <app-book-line [book]="item.book">
                @if (cart.quantities().get(item.book.id); as quantity) {
                  <a mat-stroked-button routerLink="/cart">У кошику · {{ quantity }}</a>
                } @else {
                  <button
                    mat-flat-button
                    (click)="addToCart(item)"
                    [disabled]="busy() || item.book.stock === 0"
                  >
                    <mat-icon>add_shopping_cart</mat-icon> В кошик
                  </button>
                }
                <button
                  mat-icon-button
                  (click)="remove(item)"
                  [disabled]="busy()"
                  aria-label="Прибрати зі списку"
                >
                  <mat-icon>close</mat-icon>
                </button>
              </app-book-line>
            </li>
          }
        </ul>
      } @else {
        <div class="empty">
          <mat-icon>favorite</mat-icon>
          <p>Список бажань порожній.</p>
          <p class="hint">Натисніть ♡ на сторінці книжки, щоб зберегти її на потім.</p>
          <a mat-flat-button routerLink="/catalog">До каталогу</a>
        </div>
      }
    }
  `,
  styles: `
    h1 {
      margin: 0 0 8px;
      font: var(--mat-sys-headline-small);
    }

    ul {
      list-style: none;
      margin: 0;
      padding: 0;
      max-width: 860px;

      li + li {
        border-top: 1px solid var(--mat-sys-outline-variant);
      }
    }

    .empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      padding: 64px 16px;
      color: var(--mat-sys-on-surface-variant);
      text-align: center;

      mat-icon {
        font-size: 48px;
        width: 48px;
        height: 48px;
      }

      p {
        margin: 0;
        font: var(--mat-sys-title-medium);
      }

      .hint {
        font: var(--mat-sys-body-medium);
        margin-bottom: 8px;
      }
    }
  `,
})
export class WishlistPage {
  private readonly snackBar = inject(MatSnackBar);
  protected readonly store = inject(WishlistStore);
  protected readonly cart = inject(CartStore);
  protected readonly busy = signal(false);

  constructor() {
    this.store.reload().subscribe({ error: () => undefined });
  }

  protected addToCart(item: WishlistItemDto): void {
    this.run(this.cart.add(item.book.id), 'Додано в кошик');
  }

  protected remove(item: WishlistItemDto): void {
    this.run(this.store.remove(item.book.id), 'Прибрано зі списку бажань');
  }

  private run(request: Observable<unknown>, done: string): void {
    this.busy.set(true);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.snackBar.open(done, undefined, { duration: 3000 });
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.snackBar.open(shopErrorMessage(error), 'Закрити', { duration: 6000 });
      },
    });
  }
}

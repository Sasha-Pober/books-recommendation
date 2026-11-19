import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';

import type { BookDto } from '../api/models';
import { AuthService } from '../core/auth/auth.service';
import { CartStore } from './cart.store';
import { shopErrorMessage } from './shop-errors';
import { WishlistStore } from './wishlist.store';

/**
 * «Додати в кошик» and the wishlist heart for a book. Guests are sent to sign in (and brought back);
 * managers and admins, who don't shop, see nothing.
 */
@Component({
  selector: 'app-buy-buttons',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, RouterLink],
  template: `
    @if (visible()) {
      @if (inCart(); as quantity) {
        <a mat-stroked-button routerLink="/cart">
          <mat-icon>shopping_cart_checkout</mat-icon>
          У кошику · {{ quantity }} шт.
        </a>
      } @else {
        <button mat-flat-button (click)="addToCart()" [disabled]="busy() || book().stock === 0">
          <mat-icon>add_shopping_cart</mat-icon>
          {{ book().stock === 0 ? 'Немає в наявності' : 'Додати в кошик' }}
        </button>
      }
      <button
        mat-icon-button
        class="wish"
        [class.on]="inWishlist()"
        (click)="toggleWishlist()"
        [disabled]="busy()"
        [attr.aria-pressed]="inWishlist()"
        [attr.aria-label]="inWishlist() ? 'Прибрати зі списку бажань' : 'Додати до списку бажань'"
        [matTooltip]="inWishlist() ? 'У списку бажань' : 'До списку бажань'"
      >
        <mat-icon>favorite</mat-icon>
      </button>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 8px;
    }

    .wish mat-icon {
      font-variation-settings: 'FILL' 0;
    }

    .wish.on mat-icon {
      color: #c2185b;
      font-variation-settings: 'FILL' 1;
    }
  `,
})
export class BuyButtons {
  private readonly auth = inject(AuthService);
  private readonly cart = inject(CartStore);
  private readonly wishlist = inject(WishlistStore);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  readonly book = input.required<BookDto>();

  protected readonly busy = signal(false);
  /** Guests see the buttons (to be invited to sign in); signed-in users only if they are customers. */
  protected readonly visible = computed(
    () => this.auth.isReady() && (!this.auth.isSignedIn() || this.cart.available()),
  );
  protected readonly inCart = computed(() => this.cart.quantities().get(this.book().id) ?? 0);
  protected readonly inWishlist = computed(() => this.wishlist.ids().has(this.book().id));

  protected addToCart(): void {
    if (this.signInFirst()) {
      return;
    }
    this.busy.set(true);
    this.cart.add(this.book().id).subscribe({
      next: () => {
        this.busy.set(false);
        this.snackBar
          .open('Додано в кошик', 'До кошика', { duration: 4000 })
          .onAction()
          .subscribe(() => void this.router.navigateByUrl('/cart'));
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  protected toggleWishlist(): void {
    if (this.signInFirst()) {
      return;
    }
    const adding = !this.inWishlist();
    this.busy.set(true);
    this.wishlist.toggle(this.book().id).subscribe({
      next: () => {
        this.busy.set(false);
        this.snackBar.open(
          adding ? 'Додано до списку бажань' : 'Прибрано зі списку бажань',
          undefined,
          {
            duration: 3000,
          },
        );
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  private signInFirst(): boolean {
    if (this.auth.isSignedIn()) {
      return false;
    }
    void this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
    return true;
  }

  private fail(error: unknown): void {
    this.busy.set(false);
    this.snackBar.open(shopErrorMessage(error), 'Закрити', { duration: 6000 });
    this.cart.reload().subscribe({ error: () => undefined });
  }
}

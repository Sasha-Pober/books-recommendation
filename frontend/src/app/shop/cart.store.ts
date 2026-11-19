import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Observable, tap } from 'rxjs';

import type { CartDto, OrderDto } from '../api/models';
import { AuthService } from '../core/auth/auth.service';
import { Role } from '../core/auth/roles';

/**
 * The signed-in customer's cart, shared by the toolbar badge, book pages and the cart page. Every change
 * returns the whole cart from the API, which replaces the local copy (the server is the source of truth).
 */
@Injectable({ providedIn: 'root' })
export class CartStore {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  private readonly state = signal<CartDto | null>(null);

  /** Only customers have a cart (managers and admins don't shop). */
  readonly available = computed(() => this.auth.hasRole(Role.Customer));
  readonly cart = this.state.asReadonly();
  readonly count = computed(() => this.state()?.totalQuantity ?? 0);
  readonly quantities = computed(
    () => new Map(this.state()?.items.map((i) => [i.book.id, i.quantity]) ?? []),
  );

  constructor() {
    // Load for each customer who signs in; forget on sign-out.
    effect(() => {
      const userId = this.auth.user()?.id;
      const available = this.available();
      untracked(() => {
        this.state.set(null);
        if (userId && available) {
          this.reload().subscribe({ error: () => undefined });
        }
      });
    });
  }

  reload(): Observable<CartDto> {
    return this.apply(this.http.get<CartDto>('/api/cart'));
  }

  add(bookId: string, quantity = 1): Observable<CartDto> {
    return this.apply(this.http.post<CartDto>('/api/cart', { bookId, quantity }));
  }

  setQuantity(bookId: string, quantity: number): Observable<CartDto> {
    return this.apply(this.http.put<CartDto>(`/api/cart/${bookId}`, { quantity }));
  }

  remove(bookId: string): Observable<CartDto> {
    return this.apply(this.http.delete<CartDto>(`/api/cart/${bookId}`));
  }

  clear(): Observable<CartDto> {
    return this.apply(this.http.delete<CartDto>('/api/cart'));
  }

  /** Places an order for everything in the cart; the API empties the cart. */
  checkout(): Observable<OrderDto> {
    return this.http
      .post<OrderDto>('/api/orders', null)
      .pipe(tap(() => this.state.set({ items: [], totalQuantity: 0, total: 0 })));
  }

  private apply(request: Observable<CartDto>): Observable<CartDto> {
    return request.pipe(tap((cart) => this.state.set(cart)));
  }
}

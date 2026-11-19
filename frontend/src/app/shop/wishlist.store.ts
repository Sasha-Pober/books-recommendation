import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Observable, tap } from 'rxjs';

import type { WishlistItemDto } from '../api/models';
import { AuthService } from '../core/auth/auth.service';
import { Role } from '../core/auth/roles';

/** The signed-in customer's wishlist (newest first, as the API returns it). */
@Injectable({ providedIn: 'root' })
export class WishlistStore {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  private readonly state = signal<WishlistItemDto[] | null>(null);

  readonly available = computed(() => this.auth.hasRole(Role.Customer));
  readonly items = computed(() => this.state() ?? []);
  readonly loaded = computed(() => this.state() !== null);
  readonly ids = computed(() => new Set(this.items().map((i) => i.book.id)));

  constructor() {
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

  has(bookId: string): boolean {
    return this.ids().has(bookId);
  }

  reload(): Observable<WishlistItemDto[]> {
    return this.apply(this.http.get<WishlistItemDto[]>('/api/wishlist'));
  }

  add(bookId: string): Observable<WishlistItemDto[]> {
    return this.apply(this.http.post<WishlistItemDto[]>('/api/wishlist', { bookId }));
  }

  remove(bookId: string): Observable<WishlistItemDto[]> {
    return this.apply(this.http.delete<WishlistItemDto[]>(`/api/wishlist/${bookId}`));
  }

  toggle(bookId: string): Observable<WishlistItemDto[]> {
    return this.has(bookId) ? this.remove(bookId) : this.add(bookId);
  }

  private apply(request: Observable<WishlistItemDto[]>): Observable<WishlistItemDto[]> {
    return request.pipe(tap((items) => this.state.set(items)));
  }
}

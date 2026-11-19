import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { book, bookId, cart } from '../../testing/books';
import { signIn } from '../../testing/session';
import { AuthService } from '../core/auth/auth.service';
import { BuyButtons } from './buy-buttons';
import { CartStore } from './cart.store';

describe('shopping', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  describe('CartStore', () => {
    it('loads the cart for a customer who signs in, and forgets it on sign-out', () => {
      const store = TestBed.inject(CartStore);
      const auth = TestBed.inject(AuthService);

      signIn(auth, backend);
      TestBed.tick();
      backend.expectOne('/api/cart').flush(cart({ n: 1, quantity: 2 }));
      expect(store.count()).toBe(2);
      expect(store.quantities().get(bookId(1))).toBe(2);

      auth.logout().subscribe();
      backend.expectOne('/api/auth/logout').flush(null);
      TestBed.tick();
      expect(store.cart()).toBeNull();
    });

    it('managers have no cart', () => {
      const store = TestBed.inject(CartStore);
      signIn(TestBed.inject(AuthService), backend, ['Manager']);
      TestBed.tick();

      backend.expectNone('/api/cart');
      expect(store.available()).toBe(false);
    });

    it('checkout places the order and empties the cart', () => {
      const store = TestBed.inject(CartStore);
      signIn(TestBed.inject(AuthService), backend);
      TestBed.tick();
      backend.expectOne('/api/cart').flush(cart({ n: 1, quantity: 1 }));

      let orderId = '';
      store.checkout().subscribe((order) => (orderId = order.id));
      backend
        .expectOne({ method: 'POST', url: '/api/orders' })
        .flush({ id: 'o1', status: 'Pending', total: 250, createdAt: '', items: [] });

      expect(orderId).toBe('o1');
      expect(store.count()).toBe(0);
    });
  });

  describe('BuyButtons', () => {
    async function render(stock = 3) {
      const fixture = TestBed.createComponent(BuyButtons);
      fixture.componentRef.setInput('book', book(1, { stock }));
      TestBed.tick();
      // The wishlist (for the heart) loads for customers once the buttons appear.
      backend.match('/api/wishlist').forEach((r) => r.flush([]));
      await fixture.whenStable();
      return fixture;
    }

    it('sends a guest to sign in and back to the book', async () => {
      const router = TestBed.inject(Router);
      await router.navigateByUrl('/');
      const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
      const fixture = await render();

      (fixture.nativeElement as HTMLElement)
        .querySelector<HTMLButtonElement>('button[mat-flat-button]')!
        .click();

      expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/' } });
      backend.expectNone('/api/cart');
    });

    it('adds to the cart, then links to it', async () => {
      TestBed.inject(CartStore); // created by the toolbar in the app
      signIn(TestBed.inject(AuthService), backend);
      TestBed.tick();
      backend.expectOne('/api/cart').flush(cart());
      const fixture = await render();
      const element: HTMLElement = fixture.nativeElement;

      element.querySelector<HTMLButtonElement>('button[mat-flat-button]')!.click();
      const add = backend.expectOne({ method: 'POST', url: '/api/cart' });
      expect(add.request.body).toEqual({ bookId: bookId(1), quantity: 1 });
      add.flush(cart({ n: 1, quantity: 1 }));
      await fixture.whenStable();

      expect(element.querySelector('a[href="/cart"]')?.textContent).toContain('У кошику · 1 шт.');
    });

    it('cannot add a book that is out of stock', async () => {
      TestBed.inject(CartStore); // created by the toolbar in the app
      signIn(TestBed.inject(AuthService), backend);
      TestBed.tick();
      backend.expectOne('/api/cart').flush(cart());
      const fixture = await render(0);

      const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
        'button[mat-flat-button]',
      )!;
      expect(button.disabled).toBe(true);
      expect(button.textContent).toContain('Немає в наявності');
    });
  });
});

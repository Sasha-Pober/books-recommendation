import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { bookId, cart } from '../../../testing/books';
import { signIn } from '../../../testing/session';
import { AuthService } from '../../core/auth/auth.service';
import { CartPage } from './cart-page';

describe('CartPage', () => {
  let backend: HttpTestingController;

  async function open(current = cart({ n: 1, quantity: 2 }, { n: 2, quantity: 1 })) {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    signIn(TestBed.inject(AuthService), backend);
    const fixture = TestBed.createComponent(CartPage);
    TestBed.tick();
    backend.match('/api/cart').forEach((r) => r.flush(current));
    await fixture.whenStable();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('lists the books with the total', async () => {
    const { element } = await open();

    expect(element.querySelectorAll('app-book-line')).toHaveLength(2);
    expect(element.querySelector('.summary')?.textContent).toContain('3 книги');
    expect(element.querySelector('.summary .total')?.textContent).toContain('750');
  });

  it('changes the quantity', async () => {
    const { fixture, element } = await open();

    element.querySelector<HTMLButtonElement>('button[aria-label="Більше"]')!.click();
    const put = backend.expectOne({ method: 'PUT', url: `/api/cart/${bookId(1)}` });
    expect(put.request.body).toEqual({ quantity: 3 });
    put.flush(cart({ n: 1, quantity: 3 }, { n: 2, quantity: 1 }));
    await fixture.whenStable();

    expect(element.querySelector('.summary')?.textContent).toContain('4 книги');
  });

  it('checkout opens the new order', async () => {
    const { element } = await open();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    element.querySelector<HTMLButtonElement>('button.checkout')!.click();
    backend
      .expectOne({ method: 'POST', url: '/api/orders' })
      .flush({ id: 'o1', status: 'Pending', total: 750, createdAt: '', items: [] });

    expect(navigate).toHaveBeenCalledWith(['/orders', 'o1'], { state: { placed: true } });
  });

  it('explains a stock conflict at checkout and refreshes the cart', async () => {
    const { fixture, element } = await open();

    element.querySelector<HTMLButtonElement>('button.checkout')!.click();
    backend
      .expectOne({ method: 'POST', url: '/api/orders' })
      .flush(null, { status: 409, statusText: 'Conflict' });
    backend.expectOne('/api/cart').flush(cart({ n: 1, quantity: 2, stock: 1 }));
    await fixture.whenStable();

    expect(element.querySelector('[role=alert]')?.textContent).toContain('немає в наявності');
    expect(element.querySelector('.short')?.textContent).toContain('лише 1 шт.');
    expect(element.querySelector<HTMLButtonElement>('button.checkout')!.disabled).toBe(true);
  });

  it('shows an empty cart', async () => {
    const { element } = await open(cart());

    expect(element.textContent).toContain('Кошик порожній');
  });
});

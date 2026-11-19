import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { bookId } from '../../../testing/books';
import { signIn } from '../../../testing/session';
import { AuthService } from '../../core/auth/auth.service';
import { BookReviews } from './book-reviews';

const review = (text: string | null, score = 5) => ({
  reviewerName: 'Читач',
  score,
  reviewText: text,
  createdAt: '2026-11-01T10:00:00Z',
  updatedAt: '2026-11-01T10:00:00Z',
});

describe('BookReviews', () => {
  let backend: HttpTestingController;

  function open(customer: boolean) {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    if (customer) {
      signIn(TestBed.inject(AuthService), backend);
    }
    const fixture = TestBed.createComponent(BookReviews);
    fixture.componentRef.setInput('bookId', bookId(1));
    fixture.componentRef.setInput('returnUrl', `/books/${bookId(1)}`);
    TestBed.tick();
    backend.match('/api/cart').forEach((r) => r.flush({ items: [], totalQuantity: 0, total: 0 }));
    return fixture;
  }

  it('lists only written reviews, a page at a time', async () => {
    const fixture = open(false);
    const list = backend.expectOne((r) => r.url === `/api/books/${bookId(1)}/ratings`);
    expect(list.request.params.get('withTextOnly')).toBe('true');
    list.flush({ items: [review('Чудова книга')], page: 1, pageSize: 5, totalCount: 6 });
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('.reviews')?.textContent).toContain('Чудова книга');
    expect(element.textContent).toContain('Увійдіть');
    element.querySelector<HTMLButtonElement>('button[mat-stroked-button]')!.click();
    expect(backend.expectOne((r) => r.url.endsWith('/ratings')).request.params.get('page')).toBe(
      '2',
    );
  });

  it('shows the customer’s rating and saves a change', async () => {
    const fixture = open(true);
    backend
      .expectOne((r) => r.url.endsWith('/ratings'))
      .flush({ items: [], page: 1, pageSize: 5, totalCount: 0 });
    backend.expectOne(`/api/books/${bookId(1)}/ratings/mine`).flush(review(null, 3));
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    let rated = false;
    fixture.componentInstance.rated.subscribe(() => (rated = true));

    expect(element.querySelector<HTMLInputElement>('input[value="3"]')!.checked).toBe(true);
    const save = element.querySelector<HTMLButtonElement>('button[type=submit]')!;
    expect(save.disabled).toBe(true); // nothing changed yet

    element.querySelector<HTMLInputElement>('input[value="5"]')!.click();
    await fixture.whenStable();
    save.click();
    const post = backend.expectOne({ method: 'POST', url: `/api/books/${bookId(1)}/ratings` });
    expect(post.request.body).toEqual({ score: 5, reviewText: null });
    post.flush(review(null, 5));
    await fixture.whenStable();

    expect(rated).toBe(true);
    expect(element.textContent).toContain('Оцінку збережено');
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { AUTHOR_ID, book, bookId } from '../../../testing/books';
import { signIn } from '../../../testing/session';
import type { ModelVersionDto, PagedBooks } from '../../api/models';
import { AuthService } from '../../core/auth/auth.service';
import { AdminBooksPage } from './admin-books-page';
import { BookFormPage } from './book-form-page';
import { ModelsPage } from './models-page';

function paged(...books: ReturnType<typeof book>[]): PagedBooks {
  return { items: books, page: 1, pageSize: 20, totalCount: books.length };
}

function setUp(roles: string[]) {
  localStorage.clear();
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  });
  const backend = TestBed.inject(HttpTestingController);
  signIn(TestBed.inject(AuthService), backend, roles);
  return backend;
}

/** Answers the dialog as if the user clicked its main button (or closed it). */
function answerDialogs(result: unknown) {
  vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
    afterClosed: () => of(result),
  } as never);
}

describe('AdminBooksPage', () => {
  let backend: HttpTestingController;

  async function open() {
    backend = setUp(['Manager']);
    const fixture = TestBed.createComponent(AdminBooksPage);
    TestBed.tick();
    const request = backend.expectOne((r) => r.url === '/api/books');
    expect(request.request.params.get('sort')).toBe('Newest');
    request.flush(paged(book(1), book(2, { stock: 0 })));
    await fixture.whenStable();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('lists books, out-of-stock ones included', async () => {
    const { element } = await open();

    expect(element.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(element.querySelector('td.out')?.textContent?.trim()).toBe('0');
    expect(element.querySelector('a[href="/admin/books/new"]')).not.toBeNull();
  });

  it('deletes a book after confirmation', async () => {
    const { fixture, element } = await open();
    answerDialogs(true);

    element.querySelector<HTMLButtonElement>('button.delete')!.click();
    backend.expectOne({ method: 'DELETE', url: `/api/admin/books/${bookId(1)}` }).flush(null);
    TestBed.tick();
    backend.expectOne((r) => r.url === '/api/books').flush(paged(book(2)));
    await fixture.whenStable();

    expect(element.querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it('explains why an ordered book cannot be deleted', async () => {
    const { fixture, element } = await open();
    answerDialogs(true);

    element.querySelector<HTMLButtonElement>('button.delete')!.click();
    backend
      .expectOne({ method: 'DELETE', url: `/api/admin/books/${bookId(1)}` })
      .flush(null, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();

    expect(element.querySelector('[role=alert]')?.textContent).toContain('встановіть кількість 0');
  });

  it('does nothing when deletion is cancelled', async () => {
    const { element } = await open();
    answerDialogs(undefined);

    element.querySelector<HTMLButtonElement>('button.delete')!.click();

    backend.expectNone({ method: 'DELETE' });
  });
});

describe('BookFormPage', () => {
  let backend: HttpTestingController;

  async function open(id?: string) {
    backend = setUp(['Manager']);
    const fixture = TestBed.createComponent(BookFormPage);
    if (id) {
      fixture.componentRef.setInput('id', id);
    }
    TestBed.tick();
    backend
      .match('/api/genres')
      .forEach((r) => r.flush([{ id: 'g1', name: 'Поезія', parentId: null }]));
    backend
      .match('/api/publishers')
      .forEach((r) => r.flush([{ id: 'p1', name: 'А-ба-ба-га-ла-ма-га' }]));
    backend.match('/api/series').forEach((r) => r.flush([]));
    if (id) {
      backend.expectOne(`/api/books/${id}`).flush(
        book(1, {
          annotation: 'Вірші',
          genres: [{ id: 'g1', name: 'Поезія', parentId: null }],
          publisher: { id: 'p1', name: 'А-ба-ба-га-ла-ма-га' },
        }),
      );
    }
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const type = (name: string, value: string) => {
      const input = element.querySelector<HTMLInputElement>(`[formcontrolname="${name}"]`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const submit = () => element.querySelector<HTMLFormElement>('form')!.requestSubmit();
    return { fixture, element, type, submit };
  }

  it('fills the form with the book and saves changes', async () => {
    const { fixture, type, submit } = await open(bookId(1));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    type('price', '199.5');
    type('stock', '0');
    submit();
    const put = backend.expectOne({ method: 'PUT', url: `/api/admin/books/${bookId(1)}` });
    expect(put.request.body).toEqual({
      title: 'Книга 1',
      isbn: null,
      annotation: 'Вірші',
      language: 'uk',
      year: 2020,
      pages: 100,
      coverImageUrl: null,
      price: 199.5,
      stock: 0,
      authorIds: [AUTHOR_ID],
      genreIds: ['g1'],
      publisherId: 'p1',
      seriesId: null,
    });
    put.flush(book(1));
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/books', bookId(1)]);
  });

  it('requires a title, a price and at least one author', async () => {
    const { fixture, element, submit } = await open();

    submit();
    await fixture.whenStable();

    backend.expectNone({ method: 'POST', url: '/api/admin/books' });
    expect(element.querySelector('.field-error')?.textContent).toContain('автора');
    expect(element.textContent).toContain('Введіть назву');
    expect(element.textContent).toContain('Вкажіть ціну');
  });

  it('rejects an invalid cover link and ISBN', async () => {
    const { fixture, element, type } = await open();

    type('coverImageUrl', 'cover.jpg');
    type('isbn', '12');
    element.querySelector<HTMLFormElement>('form')!.requestSubmit();
    await fixture.whenStable();

    expect(element.textContent).toContain('http:// або https://');
    expect(element.textContent).toContain('ISBN-10 або ISBN-13');
  });

  it('shows the 404 page for a deleted book', async () => {
    backend = setUp(['Manager']);
    const fixture = TestBed.createComponent(BookFormPage);
    fixture.componentRef.setInput('id', bookId(9));
    TestBed.tick();
    backend
      .expectOne(`/api/books/${bookId(9)}`)
      .flush(null, { status: 404, statusText: 'Not Found' });
    backend.match(() => true).forEach((r) => r.flush([]));
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Книгу не знайдено');
  });
});

describe('ModelsPage', () => {
  let backend: HttpTestingController;

  const version = (id: number, ndcg: number, isActive: boolean): ModelVersionDto => ({
    id,
    name: `hybrid-${id}`,
    trainedAt: '2026-11-20T10:00:00Z',
    isActive,
    metrics: {
      hybrid: { test: { 'ndcg@10': ndcg, users: 1000 }, cold_start: { 'ndcg@10': ndcg / 2 } },
      popularity: { test: { 'ndcg@10': 0.05 }, cold_start: { 'ndcg@10': 0 } },
    },
  });

  async function open(models: ModelVersionDto[]) {
    backend = setUp(['Admin']);
    const fixture = TestBed.createComponent(ModelsPage);
    TestBed.tick();
    backend.expectOne('/api/admin/models').flush(models);
    await fixture.whenStable();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('shows each version with its metrics and the difference from the active one', async () => {
    const { element } = await open([version(2, 0.32, false), version(1, 0.3, true)]);

    const cards = element.querySelectorAll('.version');
    expect(cards).toHaveLength(2);
    expect(cards[0].textContent).toContain('0,3200');
    expect(cards[0].querySelector('.delta.up')?.textContent).toContain('+0,0200');
    expect(cards[1].querySelector('.badge')?.textContent).toContain('Активна');
    expect(cards[1].querySelector('button.activate')).toBeNull();
  });

  it('activates a version after confirmation', async () => {
    const { fixture, element } = await open([version(2, 0.32, false), version(1, 0.3, true)]);
    answerDialogs(true);

    element.querySelector<HTMLButtonElement>('button.activate')!.click();
    backend
      .expectOne({ method: 'POST', url: '/api/admin/models/2/activate' })
      .flush(version(2, 0.32, true));
    TestBed.tick();
    backend.expectOne('/api/admin/models').flush([version(2, 0.32, true), version(1, 0.3, false)]);
    await fixture.whenStable();

    expect(element.querySelectorAll('.version')[0].querySelector('.badge')).not.toBeNull();
  });

  it('keeps the active version when the recommendation service cannot load the model', async () => {
    const { fixture, element } = await open([version(2, 0.32, false), version(1, 0.3, true)]);
    answerDialogs(true);

    element.querySelector<HTMLButtonElement>('button.activate')!.click();
    backend
      .expectOne({ method: 'POST', url: '/api/admin/models/2/activate' })
      .flush(null, { status: 503, statusText: 'Service Unavailable' });
    TestBed.tick();
    backend.expectOne('/api/admin/models').flush([version(2, 0.32, false), version(1, 0.3, true)]);
    await fixture.whenStable();

    expect(element.querySelector('[role=alert]')?.textContent).toContain(
      'Активна версія не змінилась',
    );
  });

  it('explains what to do when no model has been imported', async () => {
    const { element } = await open([]);

    expect(element.textContent).toContain('Ще немає імпортованих моделей');
    expect(element.textContent).toContain('recsys import-model');
  });
});

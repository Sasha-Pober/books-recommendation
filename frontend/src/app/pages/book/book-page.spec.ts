import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AUTHOR_ID, book, bookId, recommended } from '../../../testing/books';
import { BookPage } from './book-page';

describe('BookPage', () => {
  let backend: HttpTestingController;

  function open(id: string) {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(BookPage);
    fixture.componentRef.setInput('id', id);
    TestBed.tick();
    return fixture;
  }

  afterEach(() => history.replaceState(null, ''));

  it('shows the book with links into the catalog, and «Схожі книги» with their reasons', async () => {
    const fixture = open(bookId(1));
    backend.expectOne(`/api/books/${bookId(1)}`).flush(
      book(1, {
        title: 'Маруся Чурай',
        genres: [{ id: 'g1', name: 'Поезія', parentId: null }],
        publisher: { id: 'p1', name: 'А-ба-ба-га-ла-ма-га' },
      }),
    );
    backend
      .expectOne((r) => r.url === `/api/books/${bookId(1)}/similar`)
      .flush({
        source: 'baseline',
        modelVersionId: null,
        items: [recommended(2), recommended(3, 'Схожа за змістом і жанром')],
      });
    TestBed.tick();
    backend
      .match('/api/events')
      .forEach((r) => r.flush(null, { status: 202, statusText: 'Accepted' }));
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('h1')?.textContent).toBe('Маруся Чурай');
    const author = element.querySelector<HTMLAnchorElement>('.authors a')!;
    expect(author.getAttribute('href')).toBe(`/catalog?author=${AUTHOR_ID}`);
    expect(element.querySelector<HTMLAnchorElement>('a[mat-chip]')!.getAttribute('href')).toBe(
      '/catalog?genre=g1',
    );
    expect(element.textContent).toContain('А-ба-ба-га-ла-ма-га');
    expect(element.textContent).toContain('Українська');
    expect(element.querySelector('app-recommendation-row h2')?.textContent).toBe('Схожі книги');
    expect(element.querySelectorAll('app-recommendation-row app-book-card')).toHaveLength(2);
    expect(element.textContent).toContain('Схожа за змістом і жанром');
  });

  it('records one view, crediting the recommendation it was opened from', async () => {
    history.replaceState({ recommendationLogId: 42 }, '');
    open(bookId(1));

    backend.expectOne(`/api/books/${bookId(1)}`).flush(book(1));
    // The response reaches the resource asynchronously.
    const event = await vi.waitFor(() => {
      TestBed.tick();
      return backend.expectOne('/api/events');
    });

    expect(event.request.body).toEqual({
      events: [{ bookId: bookId(1), type: 'View', recommendationLogId: 42 }],
    });
    expect(event.request.keepalive).toBe(true);
    TestBed.tick();
    backend.expectNone('/api/events');
  });

  it('says when the book does not exist', async () => {
    const fixture = open(bookId(9));
    backend
      .expectOne(`/api/books/${bookId(9)}`)
      .flush(null, { status: 404, statusText: 'Not Found' });
    backend
      .match((r) => r.url.endsWith('/similar'))
      .forEach((r) => r.flush(null, { status: 404, statusText: 'Not Found' }));
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toBe(
      'Книгу не знайдено',
    );
    backend.expectNone('/api/events');
  });

  it('treats a malformed id as not found without calling the API', async () => {
    const fixture = open('not-a-book');
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toBe(
      'Книгу не знайдено',
    );
    backend.expectNone(() => true);
  });

  it('goes without «Схожі книги» when they fail to load', async () => {
    const fixture = open(bookId(1));
    backend.expectOne(`/api/books/${bookId(1)}`).flush(book(1));
    backend
      .expectOne((r) => r.url.endsWith('/similar'))
      .flush(null, { status: 503, statusText: 'Service Unavailable' });
    TestBed.tick();
    backend.match('/api/events').forEach((r) => r.flush(null));
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('h1')?.textContent).toBe('Книга 1');
    expect(element.querySelector('app-recommendation-row')).toBeNull();
  });
});

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import type { BookDto, PagedBooks } from '../../api/models';
import { CatalogPage } from './catalog-page';

function book(n: number, overrides: Partial<BookDto> = {}): BookDto {
  return {
    id: `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`,
    title: `Книга ${n}`,
    isbn: null,
    annotation: null,
    language: 'uk',
    year: 2020,
    pages: 100,
    coverImageUrl: null,
    price: 250,
    stock: 3,
    authors: [{ id: 'a0000000-0000-4000-8000-000000000001', fullName: 'Ліна Костенко', bio: null }],
    genres: [],
    publisher: null,
    series: null,
    averageRating: 4.5,
    ratingsCount: 12,
    ...overrides,
  };
}

const AUTHOR = 'a0000000-0000-4000-8000-000000000001';

describe('CatalogPage', () => {
  let backend: HttpTestingController;
  let router: Router;

  async function open(url: string) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'catalog', component: CatalogPage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    const fixture = TestBed.createComponent(CatalogPage);
    // Render and send the requests; whenStable() would wait for their responses.
    TestBed.tick();
    backend.match('/api/genres').forEach((r) => r.flush([]));
    return fixture;
  }

  function respond(page: Partial<PagedBooks>) {
    const request = backend.expectOne((r) => r.url === '/api/books');
    request.flush({ items: [], page: 1, pageSize: 24, totalCount: 0, totalPages: 0, ...page });
    return request.request;
  }

  it('loads the books for the URL and shows the count in Ukrainian', async () => {
    const fixture = await open('/catalog?search=Костенко&inStock=1');

    const request = respond({ items: [book(1), book(2, { stock: 0 })], totalCount: 2 });
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;

    expect(request.params.get('search')).toBe('Костенко');
    expect(request.params.get('inStockOnly')).toBe('true');
    expect(request.params.get('sort')).toBe('Relevance');
    expect(element.querySelector('h1')?.textContent).toContain('«Костенко»');
    expect(element.querySelector('.count')?.textContent).toContain('2 книги');
    expect(element.querySelectorAll('app-book-card')).toHaveLength(2);
    expect(element.textContent).toContain('Немає в наявності');
  });

  it('names the author filter from the loaded books, and removing a chip updates the URL', async () => {
    const fixture = await open(`/catalog?author=${AUTHOR}&page=3`);
    respond({ items: [book(1)], totalCount: 1 });
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const chip = [...element.querySelectorAll('mat-chip')].find((c) =>
      c.textContent?.includes('Ліна Костенко'),
    )!;
    chip.querySelector<HTMLButtonElement>('button[matChipRemove]')!.click();

    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: {} }));
  });

  it('suggests clearing filters when nothing matches', async () => {
    const fixture = await open('/catalog?lang=uk&priceTo=10');
    respond({ items: [], totalCount: 0 });
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.textContent).toContain('Нічого не знайдено');
    expect(element.textContent).toContain('Скинути фільтри');
  });

  it('offers a retry when loading fails', async () => {
    const fixture = await open('/catalog');
    backend
      .expectOne((r) => r.url === '/api/books')
      .flush(null, { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('[role=alert]')?.textContent).toContain('Не вдалося завантажити');
    element.querySelector<HTMLButtonElement>('[role=alert] button')!.click();
    TestBed.tick();
    respond({ items: [book(1)], totalCount: 1 });
    await fixture.whenStable();
    expect(element.querySelectorAll('app-book-card')).toHaveLength(1);
  });
});

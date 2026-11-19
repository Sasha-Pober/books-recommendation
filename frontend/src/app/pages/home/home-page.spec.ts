import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { RecommendedBook } from '../../api/models';
import { AuthService } from '../../core/auth/auth.service';
import { recommended } from '../../../testing/books';
import { session } from '../../../testing/session';
import { HomePage } from './home-page';

describe('HomePage', () => {
  let backend: HttpTestingController;

  function open() {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(HomePage);
    return fixture;
  }

  async function respond(
    fixture: ReturnType<typeof open>,
    source: string,
    items: RecommendedBook[],
  ) {
    const request = await vi.waitFor(() => {
      TestBed.tick();
      return backend.expectOne((r) => r.url === '/api/recommendations/home');
    });
    expect(request.request.params.get('count')).toBe('24');
    request.flush({ source, modelVersionId: source === 'ml' ? 3 : null, items });
    backend.match('/api/genres').forEach((r) => r.flush([]));
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  const headings = (element: HTMLElement) =>
    [...element.querySelectorAll('app-recommendation-row h2')].map((h) => h.textContent);

  it('shows the model’s picks as «Рекомендовано для вас», with explanations', async () => {
    const fixture = open();
    const element = await respond(fixture, 'ml', [
      recommended(1, 'Схожа на книги, які ви переглядали'),
      recommended(2, 'Читачі зі схожими смаками обирали'),
    ]);

    expect(headings(element)).toEqual(['Рекомендовано для вас']);
    expect(element.textContent).toContain('Читачі зі схожими смаками обирали');
  });

  it('splits the fallback into «Популярне зараз» and «Новинки»', async () => {
    const fixture = open();
    const trending = { ...recommended(1, 'Популярне зараз'), reason: 'Trending' as const };
    const fresh = { ...recommended(2, 'Новинка'), reason: 'NewRelease' as const };
    const element = await respond(fixture, 'baseline', [
      trending,
      fresh,
      { ...trending, book: recommended(3).book },
    ]);

    expect(headings(element)).toEqual(['Популярне зараз', 'Новинки']);
    const rows = element.querySelectorAll('app-recommendation-row');
    expect(rows[0].querySelectorAll('app-book-card')).toHaveLength(2);
    expect(rows[1].querySelectorAll('app-book-card')).toHaveLength(1);
  });

  it('reloads when the user signs in, and greets them', async () => {
    const fixture = open();
    await respond(fixture, 'baseline', [recommended(1)]);

    TestBed.inject(AuthService).login({ email: 'a@b.c', password: 'x' }).subscribe();
    backend.expectOne('/api/auth/login').flush(session());
    const element = await respond(fixture, 'ml', [recommended(5)]);

    expect(element.querySelector('h1')?.textContent).toBe('Вітаємо, Читач!');
    expect(headings(element)).toEqual(['Рекомендовано для вас']);
  });

  it('offers a retry and the catalog when recommendations fail', async () => {
    const fixture = open();
    const request = await vi.waitFor(() => {
      TestBed.tick();
      return backend.expectOne((r) => r.url === '/api/recommendations/home');
    });
    request.flush(null, { status: 500, statusText: 'Server Error' });
    backend.match('/api/genres').forEach((r) => r.flush([]));
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('[role=alert]')?.textContent).toContain(
      'Не вдалося завантажити рекомендації',
    );
    expect(element.querySelector('a[href="/catalog"]')).not.toBeNull();
  });
});

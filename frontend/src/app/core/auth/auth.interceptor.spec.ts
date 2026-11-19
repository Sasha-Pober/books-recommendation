import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { session } from '../../../testing/session';

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => backend.verify());

  function signIn(minutes = 15): string {
    const response = session(minutes);
    auth.login({ email: 'a@b.c', password: 'x' }).subscribe();
    backend.expectOne('/api/auth/login').flush(response);
    return response.accessToken;
  }

  it('guests send requests without a token', () => {
    http.get('/api/books').subscribe();

    expect(backend.expectOne('/api/books').request.headers.has('Authorization')).toBe(false);
  });

  it('adds the access token to API requests only', () => {
    const token = signIn();

    http.get('/api/cart').subscribe();
    http.get('https://example.com/data').subscribe();

    expect(backend.expectOne('/api/cart').request.headers.get('Authorization')).toBe(
      `Bearer ${token}`,
    );
    expect(backend.expectOne('https://example.com/data').request.headers.has('Authorization')).toBe(
      false,
    );
  });

  it('on 401 refreshes once and retries with the new token', async () => {
    signIn();
    let body: unknown;
    http.get('/api/cart').subscribe((b) => (body = b));

    backend.expectOne('/api/cart').flush(null, { status: 401, statusText: 'Unauthorized' });
    const fresh = session();
    await vi.waitFor(() => backend.expectOne('/api/auth/refresh').flush(fresh));
    await vi.waitFor(() => {
      const retry = backend.expectOne('/api/cart');
      expect(retry.request.headers.get('Authorization')).toBe(`Bearer ${fresh.accessToken}`);
      retry.flush({ items: [] });
    });

    expect(body).toEqual({ items: [] });
  });

  it('when the refresh fails too, the error reaches the caller and the user is signed out', async () => {
    signIn();
    let status = 0;
    http.get('/api/cart').subscribe({ error: (e) => (status = e.status) });

    backend.expectOne('/api/cart').flush(null, { status: 401, statusText: 'Unauthorized' });
    await vi.waitFor(() =>
      backend
        .expectOne('/api/auth/refresh')
        .flush(null, { status: 401, statusText: 'Unauthorized' }),
    );

    await vi.waitFor(() => expect(status).toBe(401));
    expect(auth.isSignedIn()).toBe(false);
  });
});

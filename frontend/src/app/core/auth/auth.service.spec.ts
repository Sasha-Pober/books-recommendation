import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { session } from '../../../testing/session';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let backend: HttpTestingController;

  function create(): AuthService {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    return TestBed.inject(AuthService);
  }

  beforeEach(() => localStorage.clear());
  afterEach(() => backend.verify());

  it('a guest on a first visit makes no refresh call', async () => {
    const auth = create();
    await auth.ready;

    backend.expectNone('/api/auth/refresh');
    expect(auth.isSignedIn()).toBe(false);
    expect(auth.isReady()).toBe(true);
  });

  it('signing in keeps the user and the access token in memory', async () => {
    const auth = create();
    const response = session();

    auth.login({ email: 'reader@example.com', password: 'Reader123!' }).subscribe();
    backend.expectOne('/api/auth/login').flush(response);

    expect(auth.user()?.fullName).toBe('Читач');
    expect(auth.hasRole('Customer')).toBe(true);
    expect(auth.hasRole('Admin')).toBe(false);
    let token: string | null = null;
    auth.getAccessToken().subscribe((t) => (token = t));
    expect(token).toBe(response.accessToken);
    // Nothing secret in storage: only a hint that a session exists.
    expect(JSON.stringify(localStorage)).not.toContain(response.accessToken);
  });

  it('restores the previous session from the refresh cookie', async () => {
    localStorage.setItem('bookstore.signedIn', '1');
    const auth = create();

    backend.expectOne('/api/auth/refresh').flush(session());
    await auth.ready;

    expect(auth.isSignedIn()).toBe(true);
  });

  it('an expired session signs out locally and forgets the hint', async () => {
    localStorage.setItem('bookstore.signedIn', '1');
    const auth = create();

    backend.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });
    await auth.ready;

    expect(auth.isSignedIn()).toBe(false);
    expect(localStorage.getItem('bookstore.signedIn')).toBeNull();
  });

  it('concurrent callers share one refresh (refresh tokens are single-use)', async () => {
    const auth = create();
    auth.login({ email: 'a@b.c', password: 'x' }).subscribe();
    backend.expectOne('/api/auth/login').flush(session(0)); // already expiring
    const tokens: (string | null)[] = [];

    auth.getAccessToken().subscribe((t) => tokens.push(t));
    auth.getAccessToken().subscribe((t) => tokens.push(t));
    const fresh = session();
    await Promise.resolve();
    backend.expectOne('/api/auth/refresh').flush(fresh);
    await vi.waitFor(() => expect(tokens).toHaveLength(2));

    expect(tokens).toEqual([fresh.accessToken, fresh.accessToken]);
  });

  it('logout signs out locally even if the request fails', () => {
    const auth = create();
    auth.login({ email: 'a@b.c', password: 'x' }).subscribe();
    backend.expectOne('/api/auth/login').flush(session());

    auth.logout().subscribe({ error: () => undefined });
    backend.expectOne('/api/auth/logout').flush(null, { status: 500, statusText: 'Server Error' });

    expect(auth.isSignedIn()).toBe(false);
  });
});

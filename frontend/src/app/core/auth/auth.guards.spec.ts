import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';

import { authGuard, roleGuard, safeReturnUrl } from './auth.guards';
import { AuthService } from './auth.service';

describe('auth guards', () => {
  const user = signal<{ roles: string[] } | null>(null);
  const fakeAuth = {
    ready: Promise.resolve(),
    isSignedIn: () => user() !== null,
    hasRole: (...roles: string[]) => roles.some((r) => user()?.roles.includes(r)),
  };

  beforeEach(() => {
    user.set(null);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: fakeAuth }],
    });
  });

  const run = (guard: typeof authGuard, url = '/cart') =>
    TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    ) as Promise<boolean | UrlTree>;
  const serialize = (result: boolean | UrlTree) =>
    typeof result === 'boolean' ? result : TestBed.inject(Router).serializeUrl(result);

  it('sends guests to sign in, with the way back', async () => {
    expect(serialize(await run(authGuard))).toBe('/login?returnUrl=%2Fcart');
  });

  it('lets signed-in users through', async () => {
    user.set({ roles: ['Customer'] });
    expect(await run(authGuard)).toBe(true);
  });

  it('role guard sends users without the role home', async () => {
    user.set({ roles: ['Customer'] });
    expect(serialize(await run(roleGuard('Admin'), '/admin'))).toBe('/');

    user.set({ roles: ['Admin'] });
    expect(await run(roleGuard('Manager', 'Admin'), '/admin')).toBe(true);
  });

  it('return links stay inside the app', () => {
    expect(safeReturnUrl('/catalog?search=x')).toBe('/catalog?search=x');
    expect(safeReturnUrl('//evil.example')).toBe('/');
    expect(safeReturnUrl('/\\evil.example')).toBe('/');
    expect(safeReturnUrl('https://evil.example')).toBe('/');
    expect(safeReturnUrl(undefined)).toBe('/');
  });
});

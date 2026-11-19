import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from './auth.service';
import type { Role } from './roles';

/** Signed-in users only; guests are sent to sign in and brought back afterwards. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.ready;
  return (
    auth.isSignedIn() || router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })
  );
};

/** Users with one of the roles; guests are sent to sign in, other users to the home page. */
export const roleGuard =
  (...roles: Role[]): CanActivateFn =>
  async (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);
    await auth.ready;
    if (!auth.isSignedIn()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }
    return auth.hasRole(...roles) || router.createUrlTree(['/']);
  };

/** The sign-in and registration pages: a signed-in user goes straight on. */
export const guestOnlyGuard: CanActivateFn = async (route) => {
  const auth = inject(AuthService);
  await auth.ready;
  return (
    !auth.isSignedIn() ||
    inject(Router).parseUrl(safeReturnUrl(route.queryParamMap.get('returnUrl')))
  );
};

/** Only paths inside this app (not "//evil.example" or "https://…"), so a crafted link can't redirect away. */
export function safeReturnUrl(value: string | null | undefined): string {
  return value && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\')
    ? value
    : '/';
}

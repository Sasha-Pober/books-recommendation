import type { HttpTestingController } from '@angular/common/http/testing';

import type { SessionResponse } from '../app/api/models';
import type { AuthService } from '../app/core/auth/auth.service';

/** A sign-in response whose access token expires in `minutes`. */
export function session(minutes = 15, roles = ['Customer']): SessionResponse {
  return {
    accessToken: `token-${Math.random()}`,
    accessTokenExpiresAt: new Date(Date.now() + minutes * 60_000).toISOString(),
    user: { id: 'u1', email: 'reader@example.com', fullName: 'Читач', roles },
  };
}

/** Signs in through the real AuthService against HttpTestingController. */
export function signIn(
  auth: AuthService,
  backend: HttpTestingController,
  roles = ['Customer'],
): void {
  auth.login({ email: 'reader@example.com', password: 'x' }).subscribe();
  backend.expectOne('/api/auth/login').flush(session(15, roles));
}

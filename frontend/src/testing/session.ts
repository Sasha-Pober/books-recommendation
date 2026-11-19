import type { SessionResponse } from '../app/api/models';

/** A sign-in response whose access token expires in `minutes`. */
export function session(minutes = 15, roles = ['Customer']): SessionResponse {
  return {
    accessToken: `token-${Math.random()}`,
    accessTokenExpiresAt: new Date(Date.now() + minutes * 60_000).toISOString(),
    user: { id: 'u1', email: 'reader@example.com', fullName: 'Читач', roles },
  };
}

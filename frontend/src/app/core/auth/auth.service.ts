import { HttpClient, HttpContext, HttpContextToken } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Observable, finalize, firstValueFrom, from, map, of, shareReplay, tap } from 'rxjs';

import type { LoginRequest, RegisterRequest, SessionResponse, UserDto } from '../../api/models';
import type { Role } from './roles';

/** Marks the auth endpoints' own requests, so the auth interceptor leaves them alone. */
export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);

/** Tokens this close to expiry are refreshed before use, so a request never leaves with a stale one. */
const EXPIRY_MARGIN_MS = 30_000;
/** Not a secret: only remembers that this browser has a session, so guests don't call /refresh on every visit. */
const SIGNED_IN_HINT = 'bookstore.signedIn';
const CHANNEL = 'bookstore-auth';
const REFRESH_LOCK = 'bookstore-refresh';

type AuthMessage = 'signed-in' | 'signed-out';

/**
 * The signed-in user and their access token.
 *
 * - The access token lives only in memory; the refresh token is an HttpOnly cookie the API sets, which
 *   scripts can't read. On page load the session is restored by calling /api/auth/refresh.
 * - Refresh tokens are single-use (a reused one signs the user out everywhere), and all tabs share the
 *   cookie, so refreshes are serialized across tabs with a Web Lock and within a tab by sharing one request.
 * - Other tabs are told about sign-in and sign-out through a BroadcastChannel.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly skipAuth = { context: new HttpContext().set(SKIP_AUTH, true) };
  private readonly channel =
    typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(CHANNEL);

  private readonly currentUser = signal<UserDto | null>(null);
  private readonly restored = signal(false);
  private accessToken: { value: string; expiresAt: number } | null = null;
  private refreshing: Observable<string | null> | null = null;

  /** The signed-in user, or null for a guest. */
  readonly user = this.currentUser.asReadonly();
  readonly isSignedIn = computed(() => this.currentUser() !== null);
  /** False until the session from a previous visit has been restored (or found to be absent). */
  readonly isReady = this.restored.asReadonly();

  /** Resolves once the session from a previous visit has been restored, or found to be absent. */
  readonly ready: Promise<void>;

  constructor() {
    this.ready = this.restore();
    if (this.channel) {
      this.channel.onmessage = (event: MessageEvent<AuthMessage>) => this.onOtherTab(event.data);
      inject(DestroyRef).onDestroy(() => this.channel?.close());
    }
  }

  hasRole(...roles: Role[]): boolean {
    const user = this.currentUser();
    return user !== null && roles.some((role) => user.roles.includes(role));
  }

  login(request: LoginRequest): Observable<UserDto> {
    return this.startSession(
      this.http.post<SessionResponse>('/api/auth/login', request, this.skipAuth),
    );
  }

  register(request: RegisterRequest): Observable<UserDto> {
    return this.startSession(
      this.http.post<SessionResponse>('/api/auth/register', request, this.skipAuth),
    );
  }

  /** Revokes the refresh token on the server; signs out locally even if that request fails. */
  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', null, this.skipAuth).pipe(
      finalize(() => {
        this.clear();
        this.broadcast('signed-out');
      }),
    );
  }

  /** A usable access token (refreshed first if it is about to expire), or null for a guest. */
  getAccessToken(): Observable<string | null> {
    if (this.accessToken && this.accessToken.expiresAt - Date.now() > EXPIRY_MARGIN_MS) {
      return of(this.accessToken.value);
    }
    return this.accessToken || this.refreshing ? this.refresh() : of(null);
  }

  /**
   * Exchanges the refresh token cookie for a new access token; null if the session is gone (then the user is
   * signed out locally). Concurrent callers share one request.
   */
  refresh(): Observable<string | null> {
    this.refreshing ??= from(withLock(REFRESH_LOCK, () => this.requestRefresh())).pipe(
      finalize(() => (this.refreshing = null)),
      shareReplay(1),
    );
    return this.refreshing;
  }

  private async requestRefresh(): Promise<string | null> {
    try {
      const session = await firstValueFrom(
        this.http.post<SessionResponse>('/api/auth/refresh', null, this.skipAuth),
      );
      this.apply(session);
      return session.accessToken;
    } catch {
      this.clear();
      return null;
    }
  }

  private async restore(): Promise<void> {
    try {
      if (readHint()) {
        await firstValueFrom(this.refresh());
      }
    } finally {
      this.restored.set(true);
    }
  }

  private startSession(request: Observable<SessionResponse>): Observable<UserDto> {
    return request.pipe(
      tap((session) => {
        this.apply(session);
        this.broadcast('signed-in');
      }),
      map((session) => session.user),
    );
  }

  private apply(session: SessionResponse): void {
    this.accessToken = {
      value: session.accessToken,
      expiresAt: Date.parse(session.accessTokenExpiresAt),
    };
    this.currentUser.set(session.user);
    writeHint(true);
  }

  private clear(): void {
    this.accessToken = null;
    this.currentUser.set(null);
    writeHint(false);
  }

  private onOtherTab(message: AuthMessage): void {
    if (message === 'signed-out') {
      this.clear();
    } else if (message === 'signed-in') {
      // The cookie now holds the other tab's session: get an access token for it.
      this.refresh().subscribe();
    }
  }

  private broadcast(message: AuthMessage): void {
    this.channel?.postMessage(message);
  }
}

/** Runs `task` while holding a lock shared by all tabs of this site (just runs it where Web Locks are missing). */
function withLock<T>(name: string, task: () => Promise<T>): Promise<T> {
  const locks = typeof navigator === 'undefined' ? undefined : navigator.locks;
  return locks ? locks.request(name, task) : task();
}

function readHint(): boolean {
  try {
    return localStorage.getItem(SIGNED_IN_HINT) === '1';
  } catch {
    return true; // storage blocked: just try the cookie
  }
}

function writeHint(signedIn: boolean): void {
  try {
    if (signedIn) {
      localStorage.setItem(SIGNED_IN_HINT, '1');
    } else {
      localStorage.removeItem(SIGNED_IN_HINT);
    }
  } catch {
    // storage blocked: the hint is only an optimization
  }
}

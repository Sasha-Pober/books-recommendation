import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';

import { AuthService } from './core/auth/auth.service';
import { CartStore } from './shop/cart.store';

/** The page frame: toolbar with catalog search, navigation and the account menu, then the routed page. */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatBadgeModule,
    MatButtonModule,
    MatDividerModule,
    MatIconModule,
    MatMenuModule,
    MatToolbarModule,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  protected readonly cart = inject(CartStore);

  /** The search box shows the catalog's current search (e.g. after Back, or on a shared link). */
  protected readonly searchTerm = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => {
        const tree = this.router.parseUrl(this.router.url);
        return tree.root.children['primary']?.segments[0]?.path === 'catalog'
          ? (tree.queryParamMap.get('search') ?? '')
          : '';
      }),
    ),
    { initialValue: '' },
  );

  protected search(term: string): void {
    const query = term.trim();
    void this.router.navigate(['/catalog'], { queryParams: query ? { search: query } : {} });
  }

  /** Back to the sign-in page's return link: the page the user is on (unless it is an auth page). */
  protected currentUrl(): string | null {
    const url = this.router.url;
    return url.startsWith('/login') || url.startsWith('/register') ? null : url;
  }

  protected logout(): void {
    this.auth.logout().subscribe({
      complete: () => void this.router.navigateByUrl('/'),
      error: () => void this.router.navigateByUrl('/'),
    });
  }
}

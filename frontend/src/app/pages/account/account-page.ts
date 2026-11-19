import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { Role } from '../../core/auth/roles';

const ROLE_NAMES: Record<string, string> = {
  [Role.Customer]: 'Покупець',
  [Role.Manager]: 'Менеджер',
  [Role.Admin]: 'Адміністратор',
};

/** The signed-in user's profile, with links to their orders, wishlist and cart (customers). */
@Component({
  selector: 'app-account-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatCardModule, MatIconModule, RouterLink],
  template: `
    @if (auth.user(); as user) {
      <h1>Мій акаунт</h1>
      <mat-card appearance="outlined">
        <mat-card-content>
          <p>
            <strong>{{ user.fullName }}</strong>
          </p>
          <p>{{ user.email }}</p>
          <p>{{ roleNames(user.roles) }}</p>
        </mat-card-content>
      </mat-card>
      @if (isCustomer()) {
        <nav class="links" aria-label="Покупки">
          <a mat-stroked-button routerLink="/orders"
            ><mat-icon>receipt_long</mat-icon> Мої замовлення</a
          >
          <a mat-stroked-button routerLink="/wishlist"
            ><mat-icon>favorite</mat-icon> Список бажань</a
          >
          <a mat-stroked-button routerLink="/cart"><mat-icon>shopping_cart</mat-icon> Кошик</a>
        </nav>
      }
    }
  `,
  styles: `
    .links {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-top: 16px;
    }
  `,
})
export class AccountPage {
  protected readonly auth = inject(AuthService);
  protected readonly isCustomer = computed(() => this.auth.hasRole(Role.Customer));

  protected roleNames(roles: string[]): string {
    return roles.map((role) => ROLE_NAMES[role] ?? role).join(', ');
  }
}

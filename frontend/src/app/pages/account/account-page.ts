import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

import { AuthService } from '../../core/auth/auth.service';
import { Role } from '../../core/auth/roles';

const ROLE_NAMES: Record<string, string> = {
  [Role.Customer]: 'Покупець',
  [Role.Manager]: 'Менеджер',
  [Role.Admin]: 'Адміністратор',
};

/** The signed-in user's profile; orders, wishlist and ratings come in step F6. */
@Component({
  selector: 'app-account-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatCardModule],
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
    }
  `,
})
export class AccountPage {
  protected readonly auth = inject(AuthService);

  protected roleNames(roles: string[]): string {
    return roles.map((role) => ROLE_NAMES[role] ?? role).join(', ');
  }
}

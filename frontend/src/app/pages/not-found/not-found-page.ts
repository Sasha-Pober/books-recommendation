import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, RouterLink],
  template: `
    <h1>Сторінку не знайдено</h1>
    <p>Можливо, її перенесли або адресу введено з помилкою.</p>
    <a mat-flat-button routerLink="/">На головну</a>
  `,
})
export class NotFoundPage {}

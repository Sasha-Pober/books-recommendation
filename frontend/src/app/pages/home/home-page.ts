import { ChangeDetectionStrategy, Component } from '@angular/core';

/** "Рекомендовано для вас" and the non-personalized collections: step F4. */
@Component({
  selector: 'app-home-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Рекомендовано для вас</h1>
    <p>Тут зʼявляться персональні рекомендації.</p>
  `,
})
export class HomePage {}

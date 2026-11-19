import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

import { plural } from '../plural';

/** "★ 4,3 (128)", or "Ще немає оцінок". */
@Component({
  selector: 'app-rating',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DecimalPipe, MatIconModule],
  template: `
    @if (average() !== null && count() > 0) {
      <span class="rating" [attr.aria-label]="label()">
        <mat-icon class="star" aria-hidden="true">star</mat-icon>
        <span aria-hidden="true">{{ average() | number: '1.1-1' }}</span>
        <span class="count" aria-hidden="true">({{ count() | number }})</span>
      </span>
    } @else {
      <span class="none">Ще немає оцінок</span>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }

    .rating {
      display: inline-flex;
      align-items: center;
      gap: 3px;
    }

    .star {
      font-size: 16px;
      width: 16px;
      height: 16px;
      color: #e8a317;
      font-variation-settings: 'FILL' 1;
    }
  `,
})
export class Rating {
  readonly average = input.required<number | null>();
  readonly count = input.required<number>();

  protected readonly label = computed(() => {
    const average = (this.average() ?? 0).toLocaleString('uk', { maximumFractionDigits: 1 });
    const count = this.count();
    return `Рейтинг ${average} з 5, ${count.toLocaleString('uk')} ${plural(count, ['оцінка', 'оцінки', 'оцінок'])}`;
  });
}

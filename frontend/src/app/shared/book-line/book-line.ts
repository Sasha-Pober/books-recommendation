import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import type { BookSummaryDto } from '../../api/models';
import { BookCover } from '../book-cover/book-cover';

/** A book as a list row (cart, wishlist, order): small cover, title, authors, price; actions are projected. */
@Component({
  selector: 'app-book-line',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BookCover, CurrencyPipe, RouterLink],
  template: `
    <a class="cover" [routerLink]="['/books', book().id]" tabindex="-1" aria-hidden="true">
      <app-book-cover [book]="book()" />
    </a>
    <div class="text">
      <a class="title" [routerLink]="['/books', book().id]">{{ book().title }}</a>
      <span class="authors">{{ authors() }}</span>
      <span class="price">
        {{ price() ?? book().price | currency: 'UAH' : 'symbol-narrow' : '1.0-2' }}
        @if (book().stock === 0) {
          <span class="out">Немає в наявності</span>
        }
      </span>
    </div>
    <div class="actions"><ng-content /></div>
  `,
  styles: `
    :host {
      display: grid;
      grid-template-columns: 72px minmax(0, 1fr) auto;
      gap: 16px;
      align-items: center;
      padding: 12px 0;
    }

    .cover {
      display: block;
    }

    .text {
      display: flex;
      flex-direction: column;
      gap: 2px;
      min-width: 0;
    }

    .title {
      font: var(--mat-sys-title-small);
      color: inherit;
      text-decoration: none;
      overflow-wrap: anywhere;

      &:hover {
        text-decoration: underline;
      }
    }

    .authors {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }

    .price {
      margin-top: 4px;
      font: var(--mat-sys-body-large);
    }

    .out {
      margin-left: 8px;
      font: var(--mat-sys-label-small);
      color: var(--mat-sys-error);
    }

    .actions {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    @media (max-width: 560px) {
      :host {
        grid-template-columns: 56px minmax(0, 1fr);
      }

      .actions {
        grid-column: 2;
        flex-wrap: wrap;
      }
    }
  `,
})
export class BookLine {
  readonly book = input.required<BookSummaryDto>();
  /** Shown instead of the current price (e.g. the price paid, in an order). */
  readonly price = input<number>();

  protected readonly authors = computed(() => this.book().authors.join(', '));
}

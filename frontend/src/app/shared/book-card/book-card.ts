import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import type { BookDto } from '../../api/models';
import { BookCover } from '../book-cover/book-cover';
import { Rating } from '../rating/rating';

/** A book in a grid or a recommendation row. The whole card links to the book's page. */
@Component({
  selector: 'app-book-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BookCover, CurrencyPipe, Rating, RouterLink],
  template: `
    <a class="card" [routerLink]="['/books', book().id]">
      <app-book-cover [book]="book()" />
      <span class="title" [title]="book().title">{{ book().title }}</span>
      <span class="authors">{{ authors() }}</span>
      <app-rating [average]="book().averageRating" [count]="book().ratingsCount" />
      <span class="bottom">
        <span class="price">{{ book().price | currency: 'UAH' : 'symbol-narrow' : '1.0-2' }}</span>
        @if (book().stock === 0) {
          <span class="out-of-stock">Немає в наявності</span>
        }
      </span>
      <ng-content />
    </a>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }

    .card {
      display: flex;
      flex-direction: column;
      gap: 4px;
      height: 100%;
      padding: 8px;
      border-radius: 12px;
      color: inherit;
      text-decoration: none;
      transition: background-color 120ms;
    }

    .card:hover,
    .card:focus-visible {
      background: var(--mat-sys-surface-container);
    }

    .card:focus-visible {
      outline: 2px solid var(--mat-sys-primary);
      outline-offset: 2px;
    }

    app-book-cover {
      margin-bottom: 6px;
    }

    .title {
      font: var(--mat-sys-title-small);
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
      overflow-wrap: anywhere;
    }

    .authors {
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .bottom {
      margin-top: auto;
      padding-top: 4px;
      display: flex;
      align-items: baseline;
      flex-wrap: wrap;
      gap: 4px 8px;
    }

    .price {
      font: var(--mat-sys-title-medium);
    }

    .out-of-stock {
      font: var(--mat-sys-label-small);
      color: var(--mat-sys-error);
    }
  `,
})
export class BookCard {
  readonly book = input.required<BookDto>();

  protected readonly authors = computed(() =>
    this.book()
      .authors.map((a) => a.fullName)
      .join(', '),
  );
}

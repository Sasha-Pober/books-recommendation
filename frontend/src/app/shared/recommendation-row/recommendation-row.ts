import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

import type { RecommendedBook } from '../../api/models';
import { BookCard } from '../book-card/book-card';

/** A titled, horizontally scrolling row of recommended books, each with its short explanation. */
@Component({
  selector: 'app-recommendation-row',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BookCard, MatIconModule],
  template: `
    <h2>{{ heading() }}</h2>
    @if (loading() && !items().length) {
      <ul class="row" aria-busy="true" aria-label="Завантаження">
        @for (placeholder of placeholders; track placeholder) {
          <li class="skeleton">
            <span class="cover"></span><span class="line"></span><span class="line short"></span>
          </li>
        }
      </ul>
    } @else {
      <ul class="row">
        @for (item of items(); track item.book.id) {
          <li>
            <app-book-card [book]="item.book" [recommendationLogId]="item.logId">
              @if (item.explanation) {
                <span class="why">
                  <mat-icon aria-hidden="true">auto_awesome</mat-icon>
                  {{ item.explanation }}
                </span>
              }
            </app-book-card>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    h2 {
      margin: 0 0 8px;
      font: var(--mat-sys-title-large);
    }

    .row {
      list-style: none;
      margin: 0;
      padding: 0 0 8px;
      display: grid;
      grid-auto-flow: column;
      grid-auto-columns: 168px;
      gap: 8px;
      overflow-x: auto;
      scroll-snap-type: x proximity;
      overscroll-behavior-x: contain;

      li {
        scroll-snap-align: start;
      }
    }

    .why {
      display: flex;
      align-items: flex-start;
      gap: 4px;
      margin-top: 4px;
      font: var(--mat-sys-label-small);
      color: var(--mat-sys-primary);

      mat-icon {
        flex: none;
        font-size: 14px;
        width: 14px;
        height: 14px;
      }
    }

    .skeleton {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding: 8px;

      span {
        border-radius: 6px;
        background: var(--mat-sys-surface-container-high);
        animation: pulse 1.2s ease-in-out infinite alternate;
      }

      .cover {
        aspect-ratio: 2 / 3;
      }

      .line {
        height: 14px;
      }

      .short {
        width: 60%;
      }
    }

    @keyframes pulse {
      to {
        opacity: 0.5;
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .skeleton span {
        animation: none;
      }
    }
  `,
})
export class RecommendationRow {
  readonly heading = input.required<string>();
  readonly items = input.required<readonly RecommendedBook[]>();
  readonly loading = input(false);

  protected readonly placeholders = [1, 2, 3, 4, 5, 6];
}

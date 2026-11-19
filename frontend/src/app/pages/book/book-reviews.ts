import { DatePipe } from '@angular/common';
import { HttpClient, HttpContext } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';

import type { PagedReviews, ReviewDto } from '../../api/models';
import { AuthService } from '../../core/auth/auth.service';
import { SILENT_ERRORS } from '../../core/http/error.interceptor';
import { Role } from '../../core/auth/roles';
import { shopErrorMessage } from '../../shop/shop-errors';

const PAGE_SIZE = 5;
const MAX_REVIEW = 4000;

/** «Ваша оцінка» (customers rate 1–5 with an optional review) and the written reviews of other readers. */
@Component({
  selector: 'app-book-reviews',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    RouterLink,
  ],
  templateUrl: './book-reviews.html',
  styleUrl: './book-reviews.scss',
})
export class BookReviews {
  private readonly http = inject(HttpClient);
  protected readonly auth = inject(AuthService);

  readonly bookId = input.required<string>();
  readonly returnUrl = input.required<string>();
  /** The rating changed: the book's average needs reloading. */
  readonly rated = output<void>();

  protected readonly stars = [1, 2, 3, 4, 5];
  protected readonly maxReview = MAX_REVIEW;
  protected readonly canRate = computed(() => this.auth.hasRole(Role.Customer));

  protected readonly mine = signal<ReviewDto | null>(null);
  protected readonly score = signal(0);
  protected readonly hover = signal(0);
  protected readonly text = signal('');
  protected readonly saving = signal(false);
  protected readonly message = signal<{ text: string; error: boolean } | null>(null);
  protected readonly changed = computed(() => {
    const mine = this.mine();
    return (
      this.score() > 0 &&
      (this.score() !== mine?.score || this.text().trim() !== (mine?.reviewText ?? ''))
    );
  });

  protected readonly reviews = signal<ReviewDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loadingReviews = signal(false);
  private page = 0;

  constructor() {
    effect(() => {
      const bookId = this.bookId();
      this.resetReviews();
      this.loadMoreReviews(bookId);
    });
    effect(() => {
      const bookId = this.bookId();
      this.mine.set(null);
      this.score.set(0);
      this.text.set('');
      this.message.set(null);
      if (this.canRate()) {
        this.http
          .get<ReviewDto | null>(`/api/books/${bookId}/ratings/mine`, { context: silent() })
          .subscribe({ next: (review) => this.showMine(review), error: () => undefined });
      }
    });
  }

  protected starLabel(star: number): string {
    return ['', 'Погано', 'Так собі', 'Непогано', 'Добре', 'Чудово'][star];
  }

  protected save(): void {
    if (!this.changed() || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.message.set(null);
    const reviewText = this.text().trim() || null;
    this.http
      .post<ReviewDto>(`/api/books/${this.bookId()}/ratings`, { score: this.score(), reviewText })
      .subscribe({
        next: (review) => {
          this.saving.set(false);
          this.showMine(review);
          this.message.set({ text: 'Дякуємо! Оцінку збережено.', error: false });
          this.rated.emit();
          if (review.reviewText) {
            this.resetReviews();
            this.loadMoreReviews(this.bookId());
          }
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.message.set({ text: shopErrorMessage(error), error: true });
        },
      });
  }

  protected loadMore(): void {
    this.loadMoreReviews(this.bookId());
  }

  private showMine(review: ReviewDto | null): void {
    this.mine.set(review);
    this.score.set(review?.score ?? 0);
    this.text.set(review?.reviewText ?? '');
  }

  private resetReviews(): void {
    this.page = 0;
    this.reviews.set([]);
    this.total.set(0);
  }

  private loadMoreReviews(bookId: string): void {
    this.loadingReviews.set(true);
    const page = this.page + 1;
    this.http
      .get<PagedReviews>(`/api/books/${bookId}/ratings`, {
        params: { withTextOnly: true, page, pageSize: PAGE_SIZE },
        context: silent(),
      })
      .subscribe({
        next: (result) => {
          if (bookId !== this.bookId()) {
            return; // the user moved on to another book
          }
          this.page = page;
          this.reviews.update((shown) => [...shown, ...result.items]);
          this.total.set(result.totalCount);
          this.loadingReviews.set(false);
        },
        error: () => this.loadingReviews.set(false),
      });
  }
}

function silent(): HttpContext {
  return new HttpContext().set(SILENT_ERRORS, true);
}

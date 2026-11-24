import { CurrencyPipe } from '@angular/common';
import { HttpContext, HttpErrorResponse, httpResource } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';

import type { BookDto, RecommendationList } from '../../api/models';
import { languageName } from '../../catalog/catalog-reference';
import { AuthService } from '../../core/auth/auth.service';
import { Role } from '../../core/auth/roles';
import { InteractionEvents } from '../../core/events/interaction-events';
import { SILENT_ERRORS } from '../../core/http/error.interceptor';
import { BookCover } from '../../shared/book-cover/book-cover';
import { Rating } from '../../shared/rating/rating';
import { RecommendationRow } from '../../shared/recommendation-row/recommendation-row';
import { BuyButtons } from '../../shop/buy-buttons';
import { BookReviews } from './book-reviews';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
/** Annotations longer than this start collapsed. */
const LONG_ANNOTATION = 600;

/** A book: details, links into the catalog (authors, genres, publisher, series) and «Схожі книги». */
@Component({
  selector: 'app-book-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    BookCover,
    BookReviews,
    BuyButtons,
    CurrencyPipe,
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatProgressBarModule,
    Rating,
    RecommendationRow,
    RouterLink,
  ],
  templateUrl: './book-page.html',
  styleUrl: './book-page.scss',
})
export class BookPage {
  private readonly events = inject(InteractionEvents);
  private readonly title = inject(Title);
  protected readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  /** Managers and admins get a link to the book's edit form. */
  protected readonly canEdit = computed(() => this.auth.hasRole(Role.Manager, Role.Admin));

  /** The :id route parameter. */
  readonly id = input.required<string>();

  private readonly validId = computed(() =>
    GUID.test(this.id()) ? this.id().toLowerCase() : undefined,
  );

  protected readonly book = httpResource<BookDto>(() =>
    this.validId() ? `/api/books/${this.validId()}` : undefined,
  );
  protected readonly similar = httpResource<RecommendationList>(() =>
    this.validId()
      ? {
          url: `/api/books/${this.validId()}/similar`,
          params: { count: 12 },
          // An optional block: if it fails, the page simply goes without it.
          context: new HttpContext().set(SILENT_ERRORS, true),
        }
      : undefined,
  );

  /** Empty while loading or when the block failed (reading value() of a failed resource throws). */
  protected readonly similarItems = computed(() =>
    this.similar.hasValue() ? this.similar.value().items : [],
  );

  protected readonly notFound = computed(() => {
    const error = this.book.error();
    return !this.validId() || (error instanceof HttpErrorResponse && error.status === 404);
  });
  /** The loaded book, only once it is the one in the URL (not the previous book while the next loads). */
  protected readonly current = computed(() =>
    this.book.hasValue() && this.book.value().id === this.validId() ? this.book.value() : undefined,
  );
  protected readonly language = computed(() => {
    const code = this.current()?.language;
    return code ? languageName(code) : null;
  });
  protected readonly isLongAnnotation = computed(
    () => (this.current()?.annotation?.length ?? 0) > LONG_ANNOTATION,
  );
  protected readonly expanded = linkedSignal({ source: this.validId, computation: () => false });

  constructor() {
    // One view per opened book; with the recommendation it was opened from (passed in the navigation state).
    let recorded: string | undefined;
    effect(() => {
      const book = this.current();
      if (book && book.id !== recorded) {
        recorded = book.id;
        const logId = (history.state as { recommendationLogId?: number } | null)
          ?.recommendationLogId;
        this.events.record(book.id, 'View', logId);
      }
    });
    effect(() => {
      const book = this.current();
      if (book) {
        this.title.setTitle(`${book.title} — Книгарня`);
      } else if (this.notFound()) {
        this.title.setTitle('Книгу не знайдено — Книгарня');
      }
    });
  }
}

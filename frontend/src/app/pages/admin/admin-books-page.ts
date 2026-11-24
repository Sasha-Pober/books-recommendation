import { CurrencyPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  numberAttribute,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorIntl, MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';

import { AdminApi } from '../../admin/admin-api';
import { adminErrorMessage } from '../../admin/admin-errors';
import { ConfirmDialog, type ConfirmData } from '../../admin/dialogs';
import type { BookDto, PagedBooks } from '../../api/models';
import { UkrainianPaginatorIntl } from '../../core/paginator-intl';

const PAGE_SIZE = 20;

/** Book management: every book (also those out of stock), with search, editing and deletion. */
@Component({
  selector: 'app-admin-books-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CurrencyPipe,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatTooltipModule,
    RouterLink,
  ],
  providers: [{ provide: MatPaginatorIntl, useClass: UkrainianPaginatorIntl }],
  template: `
    <header>
      <h1>Керування книгами</h1>
      <a mat-flat-button routerLink="/admin/books/new"><mat-icon>add</mat-icon> Додати книгу</a>
    </header>

    <form class="search" role="search" (ngSubmit)="search(term.value)">
      <mat-icon aria-hidden="true">search</mat-icon>
      <input
        #term
        type="search"
        name="search"
        [value]="searchTerm() ?? ''"
        placeholder="Назва, автор або слово з анотації"
        aria-label="Пошук книг для керування"
      />
      <button mat-button type="submit">Знайти</button>
    </form>

    @if (books.isLoading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Завантаження" />
    }
    @if (error(); as message) {
      <p class="error" role="alert">{{ message }}</p>
    }

    @if (books.error() && !books.isLoading()) {
      <div class="empty" role="alert">
        <p>Не вдалося завантажити книги.</p>
        <button mat-flat-button (click)="books.reload()">Спробувати ще раз</button>
      </div>
    } @else if (page(); as result) {
      @if (result.items.length) {
        <table>
          <thead>
            <tr>
              <th>Назва</th>
              <th class="authors">Автори</th>
              <th class="num year">Рік</th>
              <th class="num">Ціна</th>
              <th class="num">Кількість</th>
              <th><span class="visually-hidden">Дії</span></th>
            </tr>
          </thead>
          <tbody>
            @for (book of result.items; track book.id) {
              <tr>
                <td>
                  <a class="title" [routerLink]="['/books', book.id]">{{ book.title }}</a>
                </td>
                <td class="authors">{{ authorNames(book) }}</td>
                <td class="num year">{{ book.year ?? '—' }}</td>
                <td class="num">{{ book.price | currency: 'UAH' : 'symbol-narrow' : '1.0-2' }}</td>
                <td class="num" [class.out]="book.stock === 0">{{ book.stock }}</td>
                <td class="actions">
                  <a
                    mat-icon-button
                    [routerLink]="['/admin/books', book.id, 'edit']"
                    [attr.aria-label]="'Редагувати «' + book.title + '»'"
                    matTooltip="Редагувати"
                  >
                    <mat-icon>edit</mat-icon>
                  </a>
                  <button
                    mat-icon-button
                    class="delete"
                    [disabled]="busy()"
                    (click)="remove(book)"
                    [attr.aria-label]="'Видалити «' + book.title + '»'"
                    matTooltip="Видалити"
                  >
                    <mat-icon>delete</mat-icon>
                  </button>
                </td>
              </tr>
            }
          </tbody>
        </table>
        <mat-paginator
          [length]="result.totalCount"
          [pageIndex]="pageIndex()"
          [pageSize]="pageSize"
          [hidePageSize]="true"
          (page)="changePage($event)"
        />
      } @else {
        <div class="empty">
          <mat-icon>search_off</mat-icon>
          <p>{{ searchTerm() ? 'Нічого не знайдено.' : 'У каталозі ще немає книг.' }}</p>
        </div>
      }
    }
  `,
  styles: `
    header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      margin-bottom: 16px;
    }

    h1 {
      margin: 0;
      font: var(--mat-sys-headline-small);
    }

    .search {
      display: flex;
      align-items: center;
      gap: 8px;
      max-width: 560px;
      margin-bottom: 16px;
      padding: 0 4px 0 12px;
      height: 44px;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 22px;

      input {
        flex: 1;
        min-width: 0;
        border: none;
        outline: none;
        background: transparent;
        color: inherit;
        font: var(--mat-sys-body-large);
      }
    }

    table {
      width: 100%;
      border-collapse: collapse;
    }

    th {
      text-align: left;
      font: var(--mat-sys-label-large);
      color: var(--mat-sys-on-surface-variant);
      padding: 8px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }

    td {
      padding: 4px 8px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }

    .num {
      text-align: right;
      white-space: nowrap;
    }

    .out {
      color: var(--mat-sys-error);
      font-weight: 500;
    }

    .title {
      color: inherit;
      font: var(--mat-sys-title-small);
    }

    .authors {
      color: var(--mat-sys-on-surface-variant);
    }

    .actions {
      white-space: nowrap;
      text-align: right;
    }

    .delete {
      color: var(--mat-sys-error);
    }

    .error {
      color: var(--mat-sys-error);
    }

    mat-paginator {
      background: transparent;
    }

    .empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      padding: 64px 16px;
      color: var(--mat-sys-on-surface-variant);

      mat-icon {
        font-size: 48px;
        width: 48px;
        height: 48px;
      }
    }

    .visually-hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip: rect(0 0 0 0);
    }

    @media (max-width: 720px) {
      .authors,
      .year {
        display: none;
      }
    }
  `,
})
export class AdminBooksPage {
  private readonly api = inject(AdminApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);

  /** ?search= and ?page= (1-based), so Back and reload keep the list where it was. */
  readonly searchTerm = input<string>(undefined, { alias: 'search' });
  readonly pageNumber = input(1, {
    alias: 'page',
    transform: (value: unknown) => Math.max(1, numberAttribute(value, 1)),
  });

  protected readonly pageSize = PAGE_SIZE;
  protected readonly pageIndex = computed(() => this.pageNumber() - 1);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly books = httpResource<PagedBooks>(() => {
    const search = this.searchTerm()?.trim();
    return {
      url: '/api/books',
      params: {
        page: this.pageNumber(),
        pageSize: PAGE_SIZE,
        ...(search ? { search } : { sort: 'Newest' }),
      },
    };
  });
  protected readonly page = computed(() =>
    this.books.hasValue() ? this.books.value() : undefined,
  );

  protected authorNames(book: BookDto): string {
    return book.authors.map((a) => a.fullName).join(', ');
  }

  protected search(term: string): void {
    const search = term.trim();
    void this.router.navigate([], { queryParams: { search: search || null, page: null } });
  }

  protected changePage(event: PageEvent): void {
    void this.router.navigate([], {
      queryParams: { page: event.pageIndex + 1 },
      queryParamsHandling: 'merge',
    });
    window.scrollTo({ top: 0 });
  }

  protected remove(book: BookDto): void {
    const data: ConfirmData = {
      title: 'Видалити книгу?',
      message: `«${book.title}» буде видалено з каталогу. Книгу, яку вже замовляли, видалити не можна — для неї встановіть кількість 0.`,
      confirm: 'Видалити',
    };
    this.dialog
      .open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }
        this.busy.set(true);
        this.error.set(null);
        this.api.deleteBook(book.id).subscribe({
          next: () => {
            this.busy.set(false);
            this.snackBar.open(`«${book.title}» видалено`, undefined, { duration: 4000 });
            this.books.reload();
          },
          error: (error: unknown) => {
            this.busy.set(false);
            this.error.set(adminErrorMessage(error, 'delete'));
          },
        });
      });
  }
}

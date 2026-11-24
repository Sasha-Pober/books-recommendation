import { HttpErrorResponse, httpResource } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  MatAutocompleteModule,
  type MatAutocompleteSelectedEvent,
} from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';

import { AdminApi } from '../../admin/admin-api';
import { adminErrorMessage } from '../../admin/admin-errors';
import { NameDialog, type NameData, type NameResult } from '../../admin/dialogs';
import type {
  AuthorDto,
  BookDto,
  PublisherDto,
  SeriesDto,
  UpsertBookRequest,
} from '../../api/models';
import { CatalogReference, LANGUAGES, languageName } from '../../catalog/catalog-reference';

/** ISBN-10 or ISBN-13, with or without hyphens and spaces. */
const ISBN = /^(?:\d[\d\s-]{8,15}[\dXx])$/;
const HTTP_URL = /^https?:\/\/\S+$/i;

/** The form for a new book (/admin/books/new) or an existing one (/admin/books/:id/edit). */
@Component({
  selector: 'app-book-form-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatAutocompleteModule,
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    ReactiveFormsModule,
    RouterLink,
  ],
  templateUrl: './book-form-page.html',
  styleUrl: './book-form-page.scss',
})
export class BookFormPage {
  private readonly api = inject(AdminApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly pageTitle = inject(Title);
  protected readonly reference = inject(CatalogReference);

  /** The :id route parameter; absent for a new book. */
  readonly id = input<string>();
  protected readonly isNew = computed(() => !this.id());

  protected readonly book = httpResource<BookDto>(() => {
    const id = this.id();
    return id ? `/api/books/${id}` : undefined;
  });
  protected readonly notFound = computed(() => {
    const error = this.book.error();
    return error instanceof HttpErrorResponse && (error.status === 404 || error.status === 400);
  });

  protected readonly publishers = httpResource<PublisherDto[]>(() => '/api/publishers', {
    defaultValue: [],
  });
  protected readonly series = httpResource<SeriesDto[]>(() => '/api/series', { defaultValue: [] });

  protected readonly form = inject(NonNullableFormBuilder).group({
    title: ['', [Validators.required, Validators.maxLength(300)]],
    isbn: ['', [Validators.maxLength(20), Validators.pattern(ISBN)]],
    annotation: ['', Validators.maxLength(10000)],
    language: ['uk' as string | null],
    year: [null as number | null, [Validators.min(0), Validators.max(2100)]],
    pages: [null as number | null, [Validators.min(1), Validators.max(100000)]],
    coverImageUrl: ['', [Validators.maxLength(2048), Validators.pattern(HTTP_URL)]],
    price: [
      null as number | null,
      [Validators.required, Validators.min(0), Validators.max(100000)],
    ],
    stock: [
      0 as number | null,
      [Validators.required, Validators.min(0), Validators.pattern(/^\d+$/)],
    ],
    genreIds: [[] as string[]],
    publisherId: [null as string | null],
    seriesId: [null as string | null],
  });

  /** Authors are kept apart from the form group: chips with an autocomplete. */
  protected readonly authors = signal<AuthorDto[]>([]);
  protected readonly authorsTouched = signal(false);
  protected readonly authorQuery = signal('');
  protected readonly authorOptions = toSignal(
    toObservable(this.authorQuery).pipe(
      map((query) => query.trim()),
      debounceTime(250),
      distinctUntilChanged(),
      switchMap((query) =>
        query.length < 2
          ? of([] as AuthorDto[])
          : this.api.searchAuthors(query).pipe(
              map((page) => page.items),
              catchError(() => of([] as AuthorDto[])),
            ),
      ),
    ),
    { initialValue: [] as AuthorDto[] },
  );
  /** Matches not yet chosen. */
  protected readonly authorSuggestions = computed(() => {
    const chosen = new Set(this.authors().map((a) => a.id));
    return this.authorOptions().filter((a) => !chosen.has(a.id));
  });

  /** Languages offered, including the book's own if it is not in the usual list. */
  protected readonly languages = computed(() => {
    const code = this.book.hasValue() ? this.book.value()?.language : null;
    return code && !LANGUAGES.some((l) => l.code === code)
      ? [...LANGUAGES, { code, name: languageName(code) }]
      : LANGUAGES;
  });

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    // Fill the form once the book has loaded.
    effect(() => {
      if (!this.book.hasValue()) {
        return;
      }
      const book = this.book.value();
      untracked(() => this.fill(book));
    });
    effect(() => {
      const title = this.isNew()
        ? 'Нова книга'
        : `Редагування: ${this.book.hasValue() ? this.book.value().title : 'книга'}`;
      this.pageTitle.setTitle(`${title} — Книгарня`);
    });
  }

  protected addAuthor(event: MatAutocompleteSelectedEvent, input: HTMLInputElement): void {
    const value = event.option.value as AuthorDto | string;
    input.value = '';
    this.authorQuery.set('');
    if (typeof value === 'string') {
      this.newAuthor(value);
      return;
    }
    this.authors.update((list) => (list.some((a) => a.id === value.id) ? list : [...list, value]));
  }

  protected removeAuthor(author: AuthorDto): void {
    this.authors.update((list) => list.filter((a) => a.id !== author.id));
    this.authorsTouched.set(true);
  }

  protected newAuthor(name = ''): void {
    this.prompt(
      { title: 'Новий автор', label: 'Імʼя та прізвище', name, maxLength: 200 },
      (result) =>
        this.api.createAuthor(result.name).subscribe({
          next: (author) => this.authors.update((list) => [...list, author]),
          error: (error: unknown) => this.error.set(adminErrorMessage(error, 'create')),
        }),
    );
  }

  protected newGenre(): void {
    this.prompt(
      {
        title: 'Новий жанр',
        label: 'Назва жанру',
        maxLength: 100,
        parents: this.reference.genreOptions(),
      },
      (result) =>
        this.api.createGenre(result.name, result.parentId).subscribe({
          next: (genre) => {
            this.reference.reloadGenres();
            const ids = this.form.controls.genreIds;
            ids.setValue([...ids.value, genre.id]);
          },
          error: (error: unknown) => this.error.set(adminErrorMessage(error, 'create')),
        }),
    );
  }

  protected newPublisher(): void {
    this.prompt(
      { title: 'Нове видавництво', label: 'Назва видавництва', maxLength: 200 },
      (result) =>
        this.api.createPublisher(result.name).subscribe({
          next: (publisher) => {
            this.publishers.reload();
            this.form.controls.publisherId.setValue(publisher.id);
          },
          error: (error: unknown) => this.error.set(adminErrorMessage(error, 'create')),
        }),
    );
  }

  protected newSeries(): void {
    this.prompt({ title: 'Нова серія', label: 'Назва серії', maxLength: 200 }, (result) =>
      this.api.createSeries(result.name).subscribe({
        next: (series) => {
          this.series.reload();
          this.form.controls.seriesId.setValue(series.id);
        },
        error: (error: unknown) => this.error.set(adminErrorMessage(error, 'create')),
      }),
    );
  }

  protected submit(): void {
    this.authorsTouched.set(true);
    if (this.form.invalid || !this.authors().length || this.saving()) {
      this.form.markAllAsTouched();
      this.error.set('Виправте позначені поля.');
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    const id = this.id();
    const request = this.request();
    (id ? this.api.updateBook(id, request) : this.api.createBook(request)).subscribe({
      next: (book) => {
        this.snackBar.open(id ? 'Зміни збережено' : 'Книгу додано до каталогу', undefined, {
          duration: 4000,
        });
        void this.router.navigate(['/books', book.id]);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.error.set(adminErrorMessage(error, 'save'));
      },
    });
  }

  private request(): UpsertBookRequest {
    const v = this.form.getRawValue();
    const text = (value: string) => value.trim() || null;
    return {
      title: v.title.trim(),
      isbn: text(v.isbn),
      annotation: text(v.annotation),
      language: v.language || null,
      year: v.year ?? null,
      pages: v.pages ?? null,
      coverImageUrl: text(v.coverImageUrl),
      price: v.price ?? 0,
      stock: v.stock ?? 0,
      authorIds: this.authors().map((a) => a.id),
      genreIds: v.genreIds,
      publisherId: v.publisherId,
      seriesId: v.seriesId,
    };
  }

  private fill(book: BookDto): void {
    this.form.reset({
      title: book.title,
      isbn: book.isbn ?? '',
      annotation: book.annotation ?? '',
      language: book.language,
      year: book.year,
      pages: book.pages,
      coverImageUrl: book.coverImageUrl ?? '',
      price: book.price,
      stock: book.stock,
      genreIds: book.genres.map((g) => g.id),
      publisherId: book.publisher?.id ?? null,
      seriesId: book.series?.id ?? null,
    });
    this.authors.set([...book.authors]);
  }

  private prompt(data: NameData, then: (result: NameResult) => void): void {
    this.error.set(null);
    this.dialog
      .open<NameDialog, NameData, NameResult>(NameDialog, { data })
      .afterClosed()
      .subscribe((result) => result && then(result));
  }
}

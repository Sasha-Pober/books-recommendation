import { DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorIntl, MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router } from '@angular/router';

import type { BookSort, PagedBooks } from '../../api/models';
import {
  CatalogQuery,
  PAGE_SIZES,
  SORTS,
  effectiveSort,
  parseCatalogQuery,
  toApiParams,
  toQueryParams,
} from '../../catalog/catalog-query';
import { CatalogReference, languageName } from '../../catalog/catalog-reference';
import { UkrainianPaginatorIntl } from '../../core/paginator-intl';
import { BookCard } from '../../shared/book-card/book-card';
import { plural } from '../../shared/plural';
import { CatalogFilters, FilterPatch } from './catalog-filters';

const SORT_LABELS: Record<BookSort, string> = {
  Relevance: 'За релевантністю',
  Rating: 'За рейтингом',
  Newest: 'Нові надходження',
  PriceAscending: 'Спочатку дешевші',
  PriceDescending: 'Спочатку дорожчі',
  Title: 'За назвою',
  Year: 'За роком видання',
};

interface ActiveFilter {
  label: string;
  /** What removing the chip changes. */
  clear: FilterPatch;
}

/** Search results and browsing: everything comes from (and goes to) the URL's query parameters. */
@Component({
  selector: 'app-catalog-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    BookCard,
    CatalogFilters,
    DecimalPipe,
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSelectModule,
  ],
  // Provided here, not app-wide: importing the paginator in the app config would put it (with mat-select)
  // into the initial bundle.
  providers: [{ provide: MatPaginatorIntl, useClass: UkrainianPaginatorIntl }],
  templateUrl: './catalog-page.html',
  styleUrl: './catalog-page.scss',
})
export class CatalogPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly reference = inject(CatalogReference);

  private readonly params = toSignal(this.route.queryParams, { requireSync: true });
  protected readonly query = computed(() => parseCatalogQuery(this.params()));

  protected readonly books = httpResource<PagedBooks>(() => ({
    url: '/api/books',
    params: toApiParams(this.query()),
  }));
  /** The last loaded page stays on screen (dimmed) while the next one loads. */
  protected readonly result = linkedSignal<PagedBooks | undefined, PagedBooks | undefined>({
    source: () => (this.books.hasValue() ? this.books.value() : undefined),
    computation: (value, previous) => value ?? previous?.value,
  });

  protected readonly filtersOpen = signal(false);
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly sort = computed(() => effectiveSort(this.query()));
  protected readonly sorts = computed(() =>
    SORTS.filter((sort) => sort !== 'Relevance' || this.query().search).map((value) => ({
      value,
      label: SORT_LABELS[value],
    })),
  );

  protected readonly heading = computed(() =>
    this.query().search ? `Пошук: «${this.query().search}»` : 'Каталог',
  );

  protected readonly countLabel = computed(() => {
    const total = this.result()?.totalCount ?? 0;
    return plural(total, ['книга', 'книги', 'книг']);
  });

  protected readonly activeFilters = computed<ActiveFilter[]>(() => {
    const q = this.query();
    const items = this.result()?.items ?? [];
    const filters: ActiveFilter[] = [];
    const price = (value: number) => `${value.toLocaleString('uk')} ₴`;

    if (q.genreId) {
      filters.push({
        label: this.reference.genreNames().get(q.genreId) ?? 'Жанр',
        clear: { genreId: undefined },
      });
    }
    if (q.authorId) {
      // Names come with the books (authors aren't searchable by id as a list); "Автор" until they load.
      const author = items.flatMap((b) => b.authors).find((a) => a.id === q.authorId);
      filters.push({
        label: author ? `Автор: ${author.fullName}` : 'Автор',
        clear: { authorId: undefined },
      });
    }
    if (q.publisherId) {
      const publisher = items.find((b) => b.publisher?.id === q.publisherId)?.publisher;
      filters.push({
        label: publisher ? `Видавництво: ${publisher.name}` : 'Видавництво',
        clear: { publisherId: undefined },
      });
    }
    if (q.seriesId) {
      const series = items.find((b) => b.series?.id === q.seriesId)?.series;
      filters.push({
        label: series ? `Серія: ${series.name}` : 'Серія',
        clear: { seriesId: undefined },
      });
    }
    if (q.language) {
      filters.push({ label: languageName(q.language), clear: { language: undefined } });
    }
    if (q.minPrice !== undefined || q.maxPrice !== undefined) {
      const label =
        q.minPrice !== undefined && q.maxPrice !== undefined
          ? `${price(q.minPrice)} – ${price(q.maxPrice)}`
          : q.minPrice !== undefined
            ? `від ${price(q.minPrice)}`
            : `до ${price(q.maxPrice!)}`;
      filters.push({ label, clear: { minPrice: undefined, maxPrice: undefined } });
    }
    if (q.minYear !== undefined || q.maxYear !== undefined) {
      const label =
        q.minYear !== undefined && q.maxYear !== undefined
          ? `${q.minYear}–${q.maxYear} рр.`
          : q.minYear !== undefined
            ? `з ${q.minYear} р.`
            : `до ${q.maxYear} р.`;
      filters.push({ label, clear: { minYear: undefined, maxYear: undefined } });
    }
    if (q.inStockOnly) {
      filters.push({ label: 'В наявності', clear: { inStockOnly: false } });
    }
    return filters;
  });

  /** Filters (except the search itself) change: back to the first page. */
  protected applyFilters(patch: FilterPatch): void {
    this.navigate({ ...this.query(), ...patch, page: 1 });
  }

  protected clearFilters(): void {
    this.navigate({
      search: this.query().search,
      inStockOnly: false,
      sort: this.query().sort,
      page: 1,
      pageSize: this.query().pageSize,
    });
  }

  protected clearSearch(): void {
    this.navigate({ ...this.query(), search: undefined, page: 1 });
  }

  protected changeSort(sort: BookSort): void {
    this.navigate({ ...this.query(), sort, page: 1 });
  }

  protected changePage(event: PageEvent): void {
    const pageSize = event.pageSize;
    // A new page size starts from the page that contains the first book currently shown.
    const page =
      pageSize === this.query().pageSize
        ? event.pageIndex + 1
        : Math.floor(((this.query().page - 1) * this.query().pageSize) / pageSize) + 1;
    this.navigate({ ...this.query(), page, pageSize });
  }

  private navigate(query: CatalogQuery): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: toQueryParams(query) });
  }
}

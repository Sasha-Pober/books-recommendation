import type { Params } from '@angular/router';

import type { BookSort } from '../api/models';

/**
 * Catalog state. It lives in the URL (so results can be bookmarked, shared and restored with Back), using
 * short query parameter names: ?search=&genre=&author=&publisher=&series=&lang=&priceFrom=&priceTo=
 * &yearFrom=&yearTo=&inStock=1&sort=&page=&size=
 */
export interface CatalogQuery {
  search?: string;
  genreId?: string;
  authorId?: string;
  publisherId?: string;
  seriesId?: string;
  language?: string;
  minPrice?: number;
  maxPrice?: number;
  minYear?: number;
  maxYear?: number;
  inStockOnly: boolean;
  /** Chosen by the user; when absent, {@link effectiveSort} applies. */
  sort?: BookSort;
  page: number;
  pageSize: number;
}

export const PAGE_SIZES = [24, 48, 96] as const;
export const DEFAULT_PAGE_SIZE = 24;

export const SORTS: readonly BookSort[] = [
  'Relevance',
  'Rating',
  'Newest',
  'PriceAscending',
  'PriceDescending',
  'Title',
  'Year',
];

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const LANGUAGE = /^[a-z]{2,3}(-[A-Za-z]{2,4})?$/;

/** Relevance when searching (only then is it meaningful), otherwise the best rated first. */
export function effectiveSort(query: Pick<CatalogQuery, 'search' | 'sort'>): BookSort {
  if (query.sort && (query.sort !== 'Relevance' || query.search)) {
    return query.sort;
  }
  return query.search ? 'Relevance' : 'Rating';
}

/** Reads the URL's query parameters; anything malformed is ignored rather than sent to the API. */
export function parseCatalogQuery(params: Params): CatalogQuery {
  const text = (name: string) => {
    const value = first(params[name])?.trim();
    return value ? value.slice(0, 200) : undefined;
  };
  const id = (name: string) => {
    const value = text(name);
    return value && GUID.test(value) ? value.toLowerCase() : undefined;
  };
  const number = (name: string, min: number, max: number) => {
    const value = Number(text(name));
    return text(name) !== undefined && Number.isFinite(value) && value >= min && value <= max
      ? value
      : undefined;
  };
  const sort = text('sort') as BookSort | undefined;
  const size = number('size', 1, 100);
  const language = text('lang');

  return {
    search: text('search'),
    genreId: id('genre'),
    authorId: id('author'),
    publisherId: id('publisher'),
    seriesId: id('series'),
    language: language && LANGUAGE.test(language) ? language : undefined,
    minPrice: number('priceFrom', 0, 1_000_000),
    maxPrice: number('priceTo', 0, 1_000_000),
    minYear: integer(number('yearFrom', -3000, 3000)),
    maxYear: integer(number('yearTo', -3000, 3000)),
    inStockOnly: text('inStock') === '1',
    sort: sort && SORTS.includes(sort) ? sort : undefined,
    page: integer(number('page', 1, 100_000)) ?? 1,
    pageSize: size && (PAGE_SIZES as readonly number[]).includes(size) ? size : DEFAULT_PAGE_SIZE,
  };
}

/** The URL's query parameters for a state; defaults are left out, so URLs stay short. */
export function toQueryParams(query: CatalogQuery): Params {
  const params: Params = {
    search: query.search,
    genre: query.genreId,
    author: query.authorId,
    publisher: query.publisherId,
    series: query.seriesId,
    lang: query.language,
    priceFrom: query.minPrice,
    priceTo: query.maxPrice,
    yearFrom: query.minYear,
    yearTo: query.maxYear,
    inStock: query.inStockOnly ? 1 : undefined,
    sort:
      query.sort && query.sort !== effectiveSort({ search: query.search }) ? query.sort : undefined,
    page: query.page > 1 ? query.page : undefined,
    size: query.pageSize !== DEFAULT_PAGE_SIZE ? query.pageSize : undefined,
  };
  return Object.fromEntries(Object.entries(params).filter(([, value]) => value !== undefined));
}

/** The /api/books query parameters for a state. */
export function toApiParams(query: CatalogQuery): Record<string, string | number | boolean> {
  const params: Record<string, string | number | boolean | undefined> = {
    search: query.search,
    genreId: query.genreId,
    authorId: query.authorId,
    publisherId: query.publisherId,
    seriesId: query.seriesId,
    language: query.language,
    minPrice: query.minPrice,
    maxPrice: query.maxPrice,
    minYear: query.minYear,
    maxYear: query.maxYear,
    inStockOnly: query.inStockOnly || undefined,
    sort: effectiveSort(query),
    page: query.page,
    pageSize: query.pageSize,
  };
  return Object.fromEntries(
    Object.entries(params).filter(([, value]) => value !== undefined),
  ) as Record<string, string | number | boolean>;
}

function first(value: unknown): string | undefined {
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === 'string' || typeof item === 'number' ? String(item) : undefined;
}

function integer(value: number | undefined): number | undefined {
  return value === undefined ? undefined : Math.trunc(value);
}

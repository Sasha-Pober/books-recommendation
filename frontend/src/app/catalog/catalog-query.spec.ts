import { effectiveSort, parseCatalogQuery, toApiParams, toQueryParams } from './catalog-query';

const GENRE = 'e81a8ef3-f9e2-5750-9e77-b1b050208aac';

describe('catalog query', () => {
  it('defaults: first page of 24, best rated first, relevance when searching', () => {
    const browse = parseCatalogQuery({});
    expect(browse).toMatchObject({ page: 1, pageSize: 24, inStockOnly: false });
    expect(effectiveSort(browse)).toBe('Rating');
    expect(effectiveSort(parseCatalogQuery({ search: 'дюна' }))).toBe('Relevance');
    // Relevance without a search means nothing: fall back.
    expect(effectiveSort(parseCatalogQuery({ sort: 'Relevance' }))).toBe('Rating');
  });

  it('round-trips through the URL, leaving defaults out', () => {
    const params = {
      search: 'Енеїда',
      genre: GENRE,
      lang: 'uk',
      priceFrom: '100',
      priceTo: '400',
      yearFrom: '1990',
      inStock: '1',
      sort: 'PriceAscending',
      page: '3',
      size: '48',
    };

    expect(toQueryParams(parseCatalogQuery(params))).toEqual({
      ...params,
      priceFrom: 100,
      priceTo: 400,
      yearFrom: 1990,
      inStock: 1,
      page: 3,
      size: 48,
    });
    expect(toQueryParams(parseCatalogQuery({ sort: 'Rating', page: '1', size: '24' }))).toEqual({});
  });

  it('ignores malformed values instead of sending them to the API', () => {
    const query = parseCatalogQuery({
      genre: 'not-a-guid',
      author: "1'; drop table books",
      lang: 'english please',
      priceFrom: '-5',
      priceTo: 'abc',
      sort: 'Random',
      page: '0',
      size: '1000',
    });

    expect(query).toEqual(parseCatalogQuery({}));
  });

  it('builds /api/books parameters with the effective sort', () => {
    expect(toApiParams(parseCatalogQuery({ search: 'sea', inStock: '1', page: '2' }))).toEqual({
      search: 'sea',
      inStockOnly: true,
      sort: 'Relevance',
      page: 2,
      pageSize: 24,
    });
  });
});

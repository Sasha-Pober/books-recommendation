import type { BookDto, RecommendedBook } from '../app/api/models';

export const AUTHOR_ID = 'a0000000-0000-4000-8000-000000000001';

/** A book with a predictable id: 00000000-0000-4000-8000-00000000000n. */
export function book(n: number, overrides: Partial<BookDto> = {}): BookDto {
  return {
    id: bookId(n),
    title: `Книга ${n}`,
    isbn: null,
    annotation: null,
    language: 'uk',
    year: 2020,
    pages: 100,
    coverImageUrl: null,
    price: 250,
    stock: 3,
    authors: [{ id: AUTHOR_ID, fullName: 'Ліна Костенко', bio: null }],
    genres: [],
    publisher: null,
    series: null,
    averageRating: 4.5,
    ratingsCount: 12,
    ...overrides,
  };
}

export function bookId(n: number): string {
  return `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
}

export function recommended(
  n: number,
  explanation = 'Того ж автора',
  logId: number | null = 100 + n,
): RecommendedBook {
  return { book: book(n), score: 1, reason: 'SameAuthor', explanation, logId };
}

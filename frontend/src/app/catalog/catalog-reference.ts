import { httpResource } from '@angular/common/http';
import { Injectable, computed } from '@angular/core';

import type { GenreDto } from '../api/models';

export interface GenreOption {
  id: string;
  name: string;
  /** 0 for top-level genres, 1 for their subgenres, … */
  depth: number;
}

/** Languages offered in the filter (ISO 639-1, as stored in books.language), Ukrainian first. */
const LANGUAGE_CODES = [
  'uk',
  'en',
  'pl',
  'de',
  'fr',
  'es',
  'it',
  'pt',
  'nl',
  'cs',
  'bg',
  'el',
  'tr',
  'ar',
  'fa',
];

const languageNames = new Intl.DisplayNames(['uk'], { type: 'language' });

/** "англійська" → "Англійська"; unknown codes are shown as they are. */
export function languageName(code: string): string {
  const name = languageNames.of(code) ?? code;
  return name.charAt(0).toLocaleUpperCase('uk') + name.slice(1);
}

export const LANGUAGES = LANGUAGE_CODES.map((code) => ({ code, name: languageName(code) }));

/** Genre tree flattened for a select: each genre followed by its subgenres, alphabetically at every level. */
export function genreOptions(genres: readonly GenreDto[]): GenreOption[] {
  const children = new Map<string | null, GenreDto[]>();
  for (const genre of genres) {
    const parent = genre.parentId ?? null;
    children.set(parent, [...(children.get(parent) ?? []), genre]);
  }
  const options: GenreOption[] = [];
  const visit = (parent: string | null, depth: number) => {
    for (const genre of [...(children.get(parent) ?? [])].sort((a, b) =>
      a.name.localeCompare(b.name, 'uk'),
    )) {
      options.push({ id: genre.id, name: genre.name, depth });
      visit(genre.id, depth + 1);
    }
  };
  visit(null, 0);
  return options;
}

/** Reference data for the catalog filters, loaded once per visit. */
@Injectable({ providedIn: 'root' })
export class CatalogReference {
  private readonly genres = httpResource<GenreDto[]>(() => '/api/genres', { defaultValue: [] });

  readonly genreOptions = computed(() => genreOptions(this.genres.value()));
  readonly genreNames = computed(() => new Map(this.genres.value().map((g) => [g.id, g.name])));

  /** After a genre is added (book management). */
  reloadGenres(): void {
    this.genres.reload();
  }
}

import { plural } from '../shared/plural';
import { genreOptions, languageName } from './catalog-reference';

describe('catalog reference data', () => {
  it('lists genres as a tree: each followed by its subgenres, in Ukrainian alphabetical order', () => {
    const options = genreOptions([
      { id: 'p', name: 'Poetry', parentId: null },
      { id: 'f', name: 'Художня література', parentId: null },
      { id: 's', name: 'Фентезі', parentId: 'f' },
      { id: 'k', name: 'Класика', parentId: 'f' },
      { id: 'c', name: 'Children', parentId: null },
    ]);

    // Ukrainian collation: Cyrillic names come before Latin ones.
    expect(options.map((o) => `${'-'.repeat(o.depth)}${o.name}`)).toEqual([
      'Художня література',
      '-Класика',
      '-Фентезі',
      'Children',
      'Poetry',
    ]);
  });

  it('names languages in Ukrainian', () => {
    expect(languageName('uk')).toBe('Українська');
    expect(languageName('en')).toBe('Англійська');
  });

  it('uses Ukrainian plural forms', () => {
    const books = (n: number) => plural(n, ['книга', 'книги', 'книг']);
    expect([1, 2, 5, 11, 21, 22, 25].map(books)).toEqual([
      'книга',
      'книги',
      'книг',
      'книг',
      'книга',
      'книги',
      'книг',
    ]);
  });
});

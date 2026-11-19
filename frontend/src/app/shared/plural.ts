const rules = new Intl.PluralRules('uk');

/** Ukrainian plural form: plural(5, ['книга', 'книги', 'книг']) → "книг". */
export function plural(count: number, [one, few, many]: readonly [string, string, string]): string {
  switch (rules.select(count)) {
    case 'one':
      return one;
    case 'few':
      return few;
    case 'many':
      return many;
    default:
      return few; // fractions: "2,5 книги"
  }
}

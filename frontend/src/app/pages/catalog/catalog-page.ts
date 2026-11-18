import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Search, filters, sorting and paging: step F3. `search` is bound from the ?search= query parameter. */
@Component({
  selector: 'app-catalog-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Каталог</h1>
    @if (search()) {
      <p>Пошук: «{{ search() }}»</p>
    }
  `,
})
export class CatalogPage {
  readonly search = input<string>();
}

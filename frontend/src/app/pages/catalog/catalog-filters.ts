import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { CatalogQuery } from '../../catalog/catalog-query';
import { CatalogReference, LANGUAGES } from '../../catalog/catalog-reference';

export type FilterPatch = Partial<Omit<CatalogQuery, 'page' | 'pageSize' | 'sort'>>;

/**
 * Genre, language, price, year and availability. Each change is emitted at once (number fields when they
 * are committed: Enter or leaving the field), and the page puts it in the URL.
 */
@Component({
  selector: 'app-catalog-filters',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
  ],
  template: `
    <mat-form-field appearance="outline" subscriptSizing="dynamic">
      <mat-label>Жанр</mat-label>
      <mat-select
        [value]="query().genreId ?? ''"
        (selectionChange)="changed.emit({ genreId: $event.value || undefined })"
      >
        <mat-option value="">Усі жанри</mat-option>
        @for (genre of reference.genreOptions(); track genre.id) {
          <mat-option [value]="genre.id" [style.padding-left.px]="16 + genre.depth * 20">{{
            genre.name
          }}</mat-option>
        }
      </mat-select>
    </mat-form-field>

    <mat-form-field appearance="outline" subscriptSizing="dynamic">
      <mat-label>Мова</mat-label>
      <mat-select
        [value]="query().language ?? ''"
        (selectionChange)="changed.emit({ language: $event.value || undefined })"
      >
        <mat-option value="">Будь-яка</mat-option>
        @for (language of languages; track language.code) {
          <mat-option [value]="language.code">{{ language.name }}</mat-option>
        }
      </mat-select>
    </mat-form-field>

    <fieldset>
      <legend>Ціна, ₴</legend>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>від</mat-label>
        <input
          matInput
          type="number"
          min="0"
          step="10"
          inputmode="numeric"
          [value]="query().minPrice ?? ''"
          (change)="changed.emit({ minPrice: number($event) })"
        />
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>до</mat-label>
        <input
          matInput
          type="number"
          min="0"
          step="10"
          inputmode="numeric"
          [value]="query().maxPrice ?? ''"
          (change)="changed.emit({ maxPrice: number($event) })"
        />
      </mat-form-field>
    </fieldset>

    <fieldset>
      <legend>Рік видання</legend>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>від</mat-label>
        <input
          matInput
          type="number"
          step="1"
          inputmode="numeric"
          [value]="query().minYear ?? ''"
          (change)="changed.emit({ minYear: number($event, true) })"
        />
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>до</mat-label>
        <input
          matInput
          type="number"
          step="1"
          inputmode="numeric"
          [value]="query().maxYear ?? ''"
          (change)="changed.emit({ maxYear: number($event, true) })"
        />
      </mat-form-field>
    </fieldset>

    <mat-slide-toggle
      [checked]="query().inStockOnly"
      (change)="changed.emit({ inStockOnly: $event.checked })"
    >
      Лише в наявності
    </mat-slide-toggle>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    fieldset {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 8px;
      margin: 0;
      padding: 0;
      border: none;
    }

    legend {
      margin-bottom: 8px;
      font: var(--mat-sys-label-large);
      color: var(--mat-sys-on-surface-variant);
    }

    mat-form-field {
      width: 100%;
    }
  `,
})
export class CatalogFilters {
  protected readonly reference = inject(CatalogReference);
  protected readonly languages = LANGUAGES;

  readonly query = input.required<CatalogQuery>();
  readonly changed = output<FilterPatch>();

  /** The committed value of a number field; empty or invalid clears the filter. */
  protected number(event: Event, whole = false): number | undefined {
    const raw = (event.target as HTMLInputElement).value.trim();
    const value = Number(raw);
    if (!raw || !Number.isFinite(value)) {
      return undefined;
    }
    return whole ? Math.trunc(value) : Math.max(0, value);
  }
}

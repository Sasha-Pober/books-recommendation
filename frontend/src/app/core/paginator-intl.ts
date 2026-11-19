import { Injectable } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';

/** Ukrainian labels for <mat-paginator>. */
@Injectable()
export class UkrainianPaginatorIntl extends MatPaginatorIntl {
  override itemsPerPageLabel = 'На сторінці:';
  override nextPageLabel = 'Наступна сторінка';
  override previousPageLabel = 'Попередня сторінка';
  override firstPageLabel = 'Перша сторінка';
  override lastPageLabel = 'Остання сторінка';

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0) {
      return '0 з 0';
    }
    const start = page * pageSize + 1;
    const end = Math.min(start + pageSize - 1, length);
    const format = new Intl.NumberFormat('uk');
    return `${format.format(start)}–${format.format(end)} з ${format.format(length)}`;
  };
}

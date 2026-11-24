import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import type { GenreOption } from '../catalog/catalog-reference';

export interface ConfirmData {
  title: string;
  message: string;
  confirm: string;
}

/** Yes/no question; closes with true when confirmed. */
@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>{{ data.message }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Скасувати</button>
      <button mat-flat-button class="confirm" [mat-dialog-close]="true">{{ data.confirm }}</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
}

export interface NameData {
  title: string;
  label: string;
  /** Initial name (e.g. what was typed in a search box). */
  name?: string;
  maxLength: number;
  /** Offers a parent (for genres). */
  parents?: readonly GenreOption[];
}

export interface NameResult {
  name: string;
  parentId: string | null;
}

/** Asks for the name of a new author, genre, publisher or series. */
@Component({
  selector: 'app-name-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    ReactiveFormsModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content>
        <mat-form-field appearance="outline">
          <mat-label>{{ data.label }}</mat-label>
          <input matInput formControlName="name" required cdkFocusInitial />
          @if (form.controls.name.hasError('required')) {
            <mat-error>Введіть назву</mat-error>
          } @else if (form.controls.name.hasError('maxlength')) {
            <mat-error>Не довше {{ data.maxLength }} символів</mat-error>
          }
        </mat-form-field>
        @if (data.parents) {
          <mat-form-field appearance="outline">
            <mat-label>Батьківський жанр</mat-label>
            <mat-select formControlName="parentId">
              <mat-option [value]="null">— Немає (верхній рівень) —</mat-option>
              @for (option of data.parents; track option.id) {
                <mat-option [value]="option.id" [style.padding-left.px]="16 + option.depth * 20">
                  {{ option.name }}
                </mat-option>
              }
            </mat-select>
          </mat-form-field>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Скасувати</button>
        <button mat-flat-button type="submit">Додати</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    mat-form-field {
      width: 100%;
      min-width: min(360px, 70vw);
    }
  `,
})
export class NameDialog {
  protected readonly data = inject<NameData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<NameDialog, NameResult>>(MatDialogRef);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [
      this.data.name?.trim() ?? '',
      [Validators.required, Validators.maxLength(this.data.maxLength)],
    ],
    parentId: [null as string | null],
  });

  protected submit(): void {
    const name = this.form.controls.name.value.trim();
    if (!name) {
      this.form.controls.name.setValue('');
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.ref.close({ name, parentId: this.form.controls.parentId.value });
  }
}

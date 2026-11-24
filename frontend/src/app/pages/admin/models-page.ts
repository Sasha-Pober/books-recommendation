import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { AdminApi } from '../../admin/admin-api';
import { adminErrorMessage } from '../../admin/admin-errors';
import { ConfirmDialog, type ConfirmData } from '../../admin/dialogs';
import {
  COLUMNS,
  bestRows,
  comparison,
  evaluatedUsers,
  formatMetric,
  metric,
  trainingSummary,
} from '../../admin/model-metrics';
import type { ModelVersionDto } from '../../api/models';

/** Headline numbers on each version's card: column indexes into COLUMNS. */
const HEADLINE = [0, 1, 2, 4, 7];

interface VersionView {
  model: ModelVersionDto;
  headline: { label: string; description: string; value: string; delta: number | null }[];
  rows: ReturnType<typeof comparison>;
  best: (number | null)[];
  users: number | null;
  training: ReturnType<typeof trainingSummary>;
}

/** Model versions (Admin): offline metrics, comparison with the baselines and switching the active version. */
@Component({
  selector: 'app-models-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    MatButtonModule,
    MatExpansionModule,
    MatIconModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
  ],
  templateUrl: './models-page.html',
  styleUrl: './models-page.scss',
})
export class ModelsPage {
  private readonly api = inject(AdminApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly columns = COLUMNS;
  protected readonly format = formatMetric;

  protected readonly models = httpResource<ModelVersionDto[]>(() => '/api/admin/models');
  /** The id being activated: the service loads the model first, which takes a few seconds. */
  protected readonly activating = signal<number | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly active = computed(() =>
    this.models.hasValue() ? (this.models.value().find((m) => m.isActive) ?? null) : null,
  );

  protected readonly versions = computed<VersionView[]>(() => {
    if (!this.models.hasValue()) {
      return [];
    }
    const active = this.active();
    return this.models.value().map((model) => {
      const rows = comparison(model.metrics);
      return {
        model,
        headline: HEADLINE.map((index) => {
          const column = COLUMNS[index];
          const value = metric(model.metrics, 'hybrid', column.split, column.key);
          const activeValue =
            active && !model.isActive
              ? metric(active.metrics, 'hybrid', column.split, column.key)
              : null;
          return {
            label: column.label,
            description: column.description,
            value: formatMetric(value, column.digits),
            delta: value !== null && activeValue !== null ? value - activeValue : null,
          };
        }),
        rows,
        best: bestRows(rows),
        users: evaluatedUsers(model.metrics),
        training: trainingSummary(model.metrics),
      };
    });
  });

  protected deltaLabel(delta: number): string {
    const sign = delta > 0 ? '+' : delta < 0 ? '−' : '±';
    return `${sign}${formatMetric(Math.abs(delta))}`;
  }

  protected activate(model: ModelVersionDto): void {
    const current = this.active();
    const data: ConfirmData = {
      title: 'Активувати версію?',
      message:
        `Рекомендації почне формувати модель «${model.name}»` +
        (current ? ` замість «${current.name}».` : '.') +
        ' Рекомендаційний сервіс спершу завантажить її, і лише після успішного завантаження перемкне версію.',
      confirm: 'Активувати',
    };
    this.dialog
      .open<ConfirmDialog, ConfirmData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }
        this.activating.set(model.id);
        this.error.set(null);
        this.api.activateModel(model.id).subscribe({
          next: (activated) => {
            this.activating.set(null);
            this.snackBar.open(`Активна модель: «${activated.name}»`, undefined, {
              duration: 5000,
            });
            this.models.reload();
          },
          error: (error: unknown) => {
            this.activating.set(null);
            this.error.set(adminErrorMessage(error, 'activate'));
            this.models.reload();
          },
        });
      });
  }
}

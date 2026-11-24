/**
 * Reading the offline metrics a model version was imported with (`metrics.json` written by `recsys train`):
 *
 *   { "hybrid": { "test": { "ndcg@10": …, … }, "cold_start": { … } },
 *     "popularity" | "tfidf" | "matrix_factorization": { same shape },   // baselines, optional
 *     "training_curve": [ { "epoch": 1, "val_ndcg@10": …, … }, … ],
 *     "alpha": { … } }
 *
 * Anything missing or malformed reads as null, so an unusual file never breaks the page.
 */

/** Models in the order they are compared; the hybrid model is the one being served. */
export const MODELS = [
  { key: 'hybrid', label: 'Гібридна (NeuMF + контент)' },
  { key: 'matrix_factorization', label: 'Матрична факторизація' },
  { key: 'tfidf', label: 'TF-IDF' },
  { key: 'popularity', label: 'Популярність' },
] as const;

export type ModelKey = (typeof MODELS)[number]['key'];
export type Split = 'test' | 'cold_start';

export interface MetricColumn {
  split: Split;
  key: string;
  label: string;
  description: string;
  /** Decimal places: 4 for 0–1 metrics, 2 for novelty (bits). */
  digits: number;
}

export const COLUMNS: readonly MetricColumn[] = [
  {
    split: 'test',
    key: 'ndcg@10',
    label: 'NDCG@10',
    description: 'Якість ранжування (основна метрика)',
    digits: 4,
  },
  {
    split: 'test',
    key: 'recall@10',
    label: 'Recall@10',
    description: 'Частка знайдених релевантних книг',
    digits: 4,
  },
  {
    split: 'test',
    key: 'hr@10',
    label: 'HR@10',
    description: 'Частка користувачів із влученням у топ-10',
    digits: 4,
  },
  {
    split: 'test',
    key: 'mrr@10',
    label: 'MRR@10',
    description: 'Позиція першого влучення',
    digits: 4,
  },
  {
    split: 'test',
    key: 'coverage@10',
    label: 'Coverage@10',
    description: 'Частка каталогу, що потрапляє в рекомендації',
    digits: 4,
  },
  {
    split: 'test',
    key: 'ild@10',
    label: 'ILD@10',
    description: 'Різноманітність списку',
    digits: 4,
  },
  {
    split: 'test',
    key: 'novelty@10',
    label: 'Novelty@10',
    description: 'Новизна (біти; більше — менш популярні книги)',
    digits: 2,
  },
  {
    split: 'cold_start',
    key: 'ndcg@10',
    label: 'NDCG@10 нові книги',
    description: 'Якість на книгах, що зʼявились після навчання',
    digits: 4,
  },
];

type Json = Record<string, unknown>;

function isObject(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** A metric of one model on one split, or null. */
export function metric(
  metrics: unknown,
  model: ModelKey,
  split: Split,
  key: string,
): number | null {
  if (!isObject(metrics)) {
    return null;
  }
  const byModel = metrics[model];
  const bySplit = isObject(byModel) ? byModel[split] : undefined;
  const value = isObject(bySplit) ? bySplit[key] : undefined;
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

/** Users the test split was evaluated on (shown next to the numbers). */
export function evaluatedUsers(metrics: unknown, split: Split = 'test'): number | null {
  return metric(metrics, 'hybrid', split, 'users');
}

export interface ComparisonRow {
  key: ModelKey;
  label: string;
  values: (number | null)[];
}

/** One row per model present in the metrics, one value per column. */
export function comparison(metrics: unknown): ComparisonRow[] {
  return MODELS.filter(({ key }) => isObject(metrics) && isObject(metrics[key])).map(
    ({ key, label }) => ({
      key,
      label,
      values: COLUMNS.map((c) => metric(metrics, key, c.split, c.key)),
    }),
  );
}

/** Per column, the index of the row with the highest value (null if no row has a value). */
export function bestRows(rows: readonly ComparisonRow[]): (number | null)[] {
  return COLUMNS.map((_, column) => {
    let best: number | null = null;
    rows.forEach((row, index) => {
      const value = row.values[column];
      if (value !== null && (best === null || value > (rows[best].values[column] ?? -Infinity))) {
        best = index;
      }
    });
    return best;
  });
}

export interface TrainingSummary {
  epochs: number;
  bestEpoch: number | null;
  bestValidationNdcg: number | null;
  minutes: number | null;
}

/** Epochs run, the best validation NDCG@10 (the weights that were kept) and the training time. */
export function trainingSummary(metrics: unknown): TrainingSummary | null {
  const curve = isObject(metrics) ? metrics['training_curve'] : undefined;
  if (!Array.isArray(curve) || !curve.length) {
    return null;
  }
  const epochs = curve.filter(isObject);
  let bestEpoch: number | null = null;
  let bestValidationNdcg: number | null = null;
  for (const record of epochs) {
    const value = record['val_ndcg@10'];
    if (typeof value === 'number' && (bestValidationNdcg === null || value > bestValidationNdcg)) {
      bestValidationNdcg = value;
      bestEpoch = typeof record['epoch'] === 'number' ? record['epoch'] : null;
    }
  }
  const seconds = epochs.at(-1)?.['seconds'];
  return {
    epochs: epochs.length,
    bestEpoch,
    bestValidationNdcg,
    minutes: typeof seconds === 'number' ? Math.round(seconds / 6) / 10 : null,
  };
}

/** "0,3125" (Ukrainian decimal comma); "—" when missing. */
export function formatMetric(value: number | null, digits = 4): string {
  return value === null
    ? '—'
    : value.toLocaleString('uk', { minimumFractionDigits: digits, maximumFractionDigits: digits });
}

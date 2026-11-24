import {
  COLUMNS,
  bestRows,
  comparison,
  evaluatedUsers,
  formatMetric,
  metric,
  trainingSummary,
} from './model-metrics';

const split = (ndcg: number) => ({ 'ndcg@10': ndcg, 'recall@10': ndcg * 2, users: 500 });

const metrics = {
  hybrid: { test: split(0.31), cold_start: split(0.12) },
  popularity: { test: split(0.2), cold_start: split(0) },
  tfidf: { test: split(0.1), cold_start: split(0.15) },
  training_curve: [
    { epoch: 1, 'val_ndcg@10': 0.2, seconds: 60 },
    { epoch: 2, 'val_ndcg@10': 0.3, seconds: 120 },
    { epoch: 3, 'val_ndcg@10': 0.28, seconds: 180 },
  ],
  alpha: { w1_user: 0.4 },
};

describe('model metrics', () => {
  it('reads a metric of a model on a split', () => {
    expect(metric(metrics, 'hybrid', 'test', 'ndcg@10')).toBe(0.31);
    expect(metric(metrics, 'tfidf', 'cold_start', 'ndcg@10')).toBe(0.15);
    expect(evaluatedUsers(metrics)).toBe(500);
  });

  it('reads missing or malformed metrics as null', () => {
    expect(metric(null, 'hybrid', 'test', 'ndcg@10')).toBeNull();
    expect(metric({ hybrid: 'oops' }, 'hybrid', 'test', 'ndcg@10')).toBeNull();
    expect(
      metric({ hybrid: { test: { 'ndcg@10': 'x' } } }, 'hybrid', 'test', 'ndcg@10'),
    ).toBeNull();
    expect(metric(metrics, 'matrix_factorization', 'test', 'ndcg@10')).toBeNull();
    expect(comparison(null)).toEqual([]);
    expect(trainingSummary({})).toBeNull();
  });

  it('compares the models that were evaluated, the hybrid one first', () => {
    const rows = comparison(metrics);

    expect(rows.map((r) => r.key)).toEqual(['hybrid', 'tfidf', 'popularity']);
    expect(rows[0].values).toHaveLength(COLUMNS.length);
  });

  it('marks the best model per column', () => {
    const rows = comparison(metrics);
    const best = bestRows(rows);
    const column = (split: string, key: string) =>
      COLUMNS.findIndex((c) => c.split === split && c.key === key);

    expect(rows[best[column('test', 'ndcg@10')]!].key).toBe('hybrid');
    expect(rows[best[column('cold_start', 'ndcg@10')]!].key).toBe('tfidf');
    expect(best[column('test', 'ild@10')]).toBeNull();
  });

  it('summarizes training: epochs, the kept epoch and the time', () => {
    expect(trainingSummary(metrics)).toEqual({
      epochs: 3,
      bestEpoch: 2,
      bestValidationNdcg: 0.3,
      minutes: 3,
    });
  });

  it('formats with a decimal comma', () => {
    expect(formatMetric(0.3125)).toBe('0,3125');
    expect(formatMetric(7.456, 2)).toBe('7,46');
    expect(formatMetric(null)).toBe('—');
  });
});

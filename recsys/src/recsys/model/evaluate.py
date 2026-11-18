"""Full-catalog evaluation of any scorer (the hybrid model or a baseline) on an EvalSet."""

from collections.abc import Callable, Sequence

import numpy as np

from recsys.model.data import EvalSet
from recsys.model.metrics import RankingMetrics

# users [batch], history items [batch, length], history weights [batch, length] → scores [batch, items]
Scorer = Callable[[np.ndarray, np.ndarray, np.ndarray], np.ndarray]


def evaluate(
    scorer: Scorer,
    eval_set: EvalSet,
    metrics: RankingMetrics,
    max_users: int | None = None,
    batch_users: int = 64,
    seed: int = 0,
) -> dict[str, float]:
    """Ranks the whole catalog for each user, skipping books the user already has."""
    positions = np.arange(len(eval_set.users))
    if max_users is not None and len(positions) > max_users:
        positions = np.sort(np.random.default_rng(seed).choice(positions, max_users, replace=False))

    k_max = max(metrics.ks)
    histories = eval_set.histories
    for start in range(0, len(positions), batch_users):
        batch = positions[start : start + batch_users]
        users = eval_set.users[batch]
        scores = np.array(scorer(users, histories.items[users], histories.weights[users]), dtype=np.float32)
        for row, i in enumerate(batch):
            known = eval_set.exclude[i]
            if len(known):
                scores[row, known] = -np.inf
        # The k best per row, best first (argpartition: no full sort of the catalog).
        top = np.argpartition(-scores, k_max - 1, axis=1)[:, :k_max]
        order = np.argsort(-np.take_along_axis(scores, top, axis=1), axis=1, kind="stable")
        metrics.add(np.take_along_axis(top, order, axis=1), [eval_set.relevant[i] for i in batch])
    return metrics.result()


def new_metrics(
    ks: Sequence[int], n_items: int, text: np.ndarray, item_counts: np.ndarray, n_train_users: int
) -> RankingMetrics:
    return RankingMetrics(
        ks, n_items, item_vectors=text, item_popularity=item_counts, n_train_users=n_train_users
    )

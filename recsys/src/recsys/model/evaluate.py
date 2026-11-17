"""Full-catalog evaluation of any scorer (the hybrid model or a baseline) on an EvalSet."""

from collections.abc import Callable, Sequence

import numpy as np
import torch
from torch import Tensor

from recsys.model.data import EvalSet
from recsys.model.metrics import RankingMetrics

# users [batch], history items [batch, length], history weights [batch, length] → scores [batch, items]
Scorer = Callable[[Tensor, Tensor, Tensor], Tensor]


def evaluate(
    scorer: Scorer,
    eval_set: EvalSet,
    metrics: RankingMetrics,
    device: torch.device,
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
        scores = scorer(
            torch.as_tensor(users, device=device),
            torch.as_tensor(histories.items[users], device=device),
            torch.as_tensor(histories.weights[users], device=device),
        ).float()
        for row, i in enumerate(batch):
            known = eval_set.exclude[i]
            if len(known):
                scores[row, torch.tensor(known, device=device)] = -torch.inf
        top = torch.topk(scores, k_max, dim=1).indices.cpu().numpy()
        metrics.add(top, [eval_set.relevant[i] for i in batch])
    return metrics.result()


def new_metrics(
    ks: Sequence[int], n_items: int, text: np.ndarray, item_counts: np.ndarray, n_train_users: int
) -> RankingMetrics:
    return RankingMetrics(
        ks, n_items, item_vectors=text, item_popularity=item_counts, n_train_users=n_train_users
    )

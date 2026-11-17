"""Ranking quality over the whole catalog: accuracy @K (Precision, Recall, HR, MRR, NDCG) and
beyond-accuracy (Coverage, ILD — intra-list diversity, Novelty)."""

from collections.abc import Sequence

import numpy as np


class RankingMetrics:
    """Accumulates per-user results; call add() per batch, then result()."""

    def __init__(
        self,
        ks: Sequence[int],
        n_items: int,
        item_vectors: np.ndarray | None = None,
        item_popularity: np.ndarray | None = None,
        n_train_users: int = 1,
    ) -> None:
        self.ks = sorted(ks)
        self.n_items = n_items
        self.vectors = None
        if item_vectors is not None:
            norms = np.linalg.norm(item_vectors, axis=1, keepdims=True)
            self.vectors = item_vectors / np.maximum(norms, 1e-12)
        # Novelty: self-information −log2 p(i) of being known by a user; rare books are more novel.
        self.self_information = None
        if item_popularity is not None:
            p = (item_popularity + 1.0) / (n_train_users + 1.0)
            self.self_information = -np.log2(np.minimum(p, 1.0))
        self.sums: dict[str, float] = {}
        self.recommended: dict[int, set[int]] = {k: set() for k in self.ks}
        self.users = 0

    def add(self, top_items: np.ndarray, relevant: Sequence[np.ndarray]) -> None:
        """top_items: [users, max(ks)] best first; relevant: per user, the item indices that count as hits."""
        for row, rel in zip(top_items, relevant, strict=True):
            self.users += 1
            hit = np.isin(row, rel)
            for k in self.ks:
                top, hits = row[:k], hit[:k]
                n_hits = int(hits.sum())
                self._add(f"precision@{k}", n_hits / k)
                self._add(f"recall@{k}", n_hits / len(rel) if len(rel) else 0.0)
                self._add(f"hr@{k}", float(n_hits > 0))
                first = np.flatnonzero(hits)
                self._add(f"mrr@{k}", 1.0 / (first[0] + 1) if first.size else 0.0)
                gains = hits / np.log2(np.arange(2, k + 2))
                ideal = (1.0 / np.log2(np.arange(2, min(len(rel), k) + 2))).sum()
                self._add(f"ndcg@{k}", gains.sum() / ideal if ideal > 0 else 0.0)
                self.recommended[k].update(top.tolist())
                if self.vectors is not None and k > 1:
                    v = self.vectors[top]
                    similarity = v @ v.T
                    self._add(f"ild@{k}", float((1 - similarity)[~np.eye(k, dtype=bool)].mean()))
                if self.self_information is not None:
                    self._add(f"novelty@{k}", float(self.self_information[top].mean()))

    def result(self) -> dict[str, float]:
        result = {name: total / max(self.users, 1) for name, total in sorted(self.sums.items())}
        for k in self.ks:
            result[f"coverage@{k}"] = len(self.recommended[k]) / self.n_items
        result["users"] = self.users
        return result

    def _add(self, name: str, value: float) -> None:
        self.sums[name] = self.sums.get(name, 0.0) + value

"""Baselines for comparison (CLAUDE.md): popularity, TF-IDF content, classic matrix factorization (BPR-MF)."""

import logging
from collections.abc import Sequence

import numpy as np
import torch
import torch.nn.functional as F
from torch import Tensor, nn

logger = logging.getLogger(__name__)


class PopularityScorer:
    """The same ranking for everyone: books with the most positive interactions in train."""

    def __init__(self, item_counts: np.ndarray, device: torch.device) -> None:
        self.scores = torch.as_tensor(np.log1p(item_counts), dtype=torch.float32, device=device)

    def __call__(self, users: Tensor, history: Tensor, weights: Tensor) -> Tensor:
        return self.scores.unsqueeze(0).expand(users.shape[0], -1).clone()


class TfidfScorer:
    """Content-only: the user profile is the decay-weighted sum of the TF-IDF vectors of their history,
    books are ranked by cosine similarity to it."""

    def __init__(self, texts: Sequence[str], max_features: int = 50_000) -> None:
        from sklearn.feature_extraction.text import TfidfVectorizer

        vectorizer = TfidfVectorizer(max_features=max_features, sublinear_tf=True, min_df=2)
        self.matrix = vectorizer.fit_transform(texts).tocsr()  # rows are L2-normalized

    def __call__(self, users: Tensor, history: Tensor, weights: Tensor) -> Tensor:
        from scipy import sparse

        h, w = history.cpu().numpy(), weights.cpu().numpy() * (history.cpu().numpy() >= 0)
        rows = np.repeat(np.arange(h.shape[0]), h.shape[1])
        selector = sparse.csr_matrix(
            (w.ravel(), (rows, h.clip(min=0).ravel())), shape=(h.shape[0], self.matrix.shape[0])
        )
        profiles = selector @ self.matrix
        scores = (profiles @ self.matrix.T).toarray()
        return torch.as_tensor(scores, dtype=torch.float32, device=users.device)


class MatrixFactorization(nn.Module):
    """BPR-MF: score = p_u · q_i. Books without train interactions keep their random initial vectors,
    which is why it can't handle cold start."""

    def __init__(self, n_users: int, n_items: int, dim: int = 64) -> None:
        super().__init__()
        self.users = nn.Embedding(n_users, dim)
        self.items = nn.Embedding(n_items, dim)
        nn.init.normal_(self.users.weight, std=0.01)
        nn.init.normal_(self.items.weight, std=0.01)

    def fit(
        self,
        train_users: np.ndarray,
        train_items: np.ndarray,
        epochs: int,
        device: torch.device,
        batch_size: int = 4096,
        lr: float = 1e-3,
        l2: float = 1e-6,
        seed: int = 0,
    ) -> "MatrixFactorization":
        self.to(device)
        optimizer = torch.optim.Adam(self.parameters(), lr=lr)
        users = torch.tensor(train_users, device=device)
        items = torch.tensor(train_items, device=device)
        generator = torch.Generator(device=device).manual_seed(seed)
        n_items = self.items.num_embeddings
        for epoch in range(epochs):
            order = torch.randperm(len(users), device=device, generator=generator)
            total = 0.0
            for start in range(0, len(order), batch_size):
                batch = order[start : start + batch_size]
                u, pos = users[batch], items[batch]
                neg = torch.randint(0, n_items, pos.shape, device=device, generator=generator)
                pu, qp, qn = self.users(u), self.items(pos), self.items(neg)
                loss = -F.logsigmoid((pu * qp).sum(-1) - (pu * qn).sum(-1)).mean()
                loss = loss + l2 * (pu.pow(2).sum() + qp.pow(2).sum() + qn.pow(2).sum()) / len(batch)
                optimizer.zero_grad()
                loss.backward()
                optimizer.step()
                total += loss.item() * len(batch)
            logger.info("BPR-MF epoch %d: loss %.4f", epoch + 1, total / len(order))
        return self

    @torch.no_grad()
    def score(self, users: Tensor, history: Tensor, weights: Tensor) -> Tensor:
        return self.users(users) @ self.items.weight.T

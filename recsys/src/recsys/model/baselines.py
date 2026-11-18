"""Baselines for comparison (CLAUDE.md): popularity, TF-IDF content, classic matrix factorization (BPR-MF)."""

import logging
from collections.abc import Sequence

import keras
import numpy as np
from keras import layers, ops

from recsys.model.network import inference_mode

logger = logging.getLogger(__name__)


class PopularityScorer:
    """The same ranking for everyone: books with the most positive interactions in train."""

    def __init__(self, item_counts: np.ndarray) -> None:
        self.scores = np.log1p(item_counts).astype(np.float32)

    def __call__(self, users: np.ndarray, history: np.ndarray, weights: np.ndarray) -> np.ndarray:
        return np.tile(self.scores, (len(users), 1))


class TfidfScorer:
    """Content-only: the user profile is the decay-weighted sum of the TF-IDF vectors of their history,
    books are ranked by cosine similarity to it."""

    def __init__(self, texts: Sequence[str], max_features: int = 50_000) -> None:
        from sklearn.feature_extraction.text import TfidfVectorizer

        vectorizer = TfidfVectorizer(max_features=max_features, sublinear_tf=True, min_df=2)
        self.matrix = vectorizer.fit_transform(texts).tocsr()  # rows are L2-normalized

    def __call__(self, users: np.ndarray, history: np.ndarray, weights: np.ndarray) -> np.ndarray:
        from scipy import sparse

        w = weights * (history >= 0)
        rows = np.repeat(np.arange(history.shape[0]), history.shape[1])
        selector = sparse.csr_matrix(
            (w.ravel(), (rows, history.clip(min=0).ravel())), shape=(history.shape[0], self.matrix.shape[0])
        )
        profiles = selector @ self.matrix
        return (profiles @ self.matrix.T).toarray().astype(np.float32)


class _MfTriples(keras.utils.PyDataset):
    """(user, positive, random negative) batches for BPR-MF; reshuffled every epoch."""

    def __init__(
        self, users: np.ndarray, items: np.ndarray, n_items: int, batch_size: int, seed: int
    ) -> None:
        super().__init__()
        self.users, self.items, self.n_items, self.batch_size = users, items, n_items, batch_size
        self.rng = np.random.default_rng(seed)
        self.order = self.rng.permutation(len(users))

    def __len__(self) -> int:
        return -(-len(self.users) // self.batch_size)

    def __getitem__(self, index: int):
        batch = self.order[index * self.batch_size : (index + 1) * self.batch_size]
        negatives = self.rng.integers(0, self.n_items, len(batch))
        inputs = {"users": self.users[batch], "positives": self.items[batch], "negatives": negatives}
        return inputs, np.zeros(len(batch), dtype=np.float32)

    def on_epoch_end(self) -> None:
        self.order = self.rng.permutation(len(self.users))


class MatrixFactorization(keras.Model):
    """BPR-MF: score = p_u · q_i. Books without train interactions keep their random initial vectors,
    which is why it can't handle cold start."""

    def __init__(self, n_users: int, n_items: int, dim: int = 64, l2: float = 1e-6, seed: int = 0) -> None:
        super().__init__()
        small = keras.initializers.RandomNormal(stddev=0.01, seed=seed)
        self.user_vectors = layers.Embedding(n_users, dim, embeddings_initializer=small)
        self.item_vectors = layers.Embedding(n_items, dim, embeddings_initializer=small)
        self.l2 = l2
        self.n_items = n_items

    def call(self, inputs):
        p = self.user_vectors(inputs["users"])
        q_pos, q_neg = self.item_vectors(inputs["positives"]), self.item_vectors(inputs["negatives"])
        difference = ops.sum(p * q_pos, axis=-1) - ops.sum(p * q_neg, axis=-1)
        penalty = (ops.sum(p * p) + ops.sum(q_pos * q_pos) + ops.sum(q_neg * q_neg)) / ops.cast(
            ops.shape(p)[0], "float32"
        )
        return {"difference": difference, "penalty": penalty}

    def compute_loss(self, x=None, y=None, y_pred=None, sample_weight=None, training=True):
        """BPR: −log σ(score(u, positive) − score(u, negative)), plus L2 on the vectors used."""
        return -ops.mean(ops.log_sigmoid(y_pred["difference"])) + self.l2 * y_pred["penalty"]

    def fit_triples(
        self,
        train_users: np.ndarray,
        train_items: np.ndarray,
        epochs: int,
        batch_size: int = 4096,
        lr: float = 1e-3,
        seed: int = 0,
    ) -> "MatrixFactorization":
        self.compile(optimizer=keras.optimizers.Adam(lr))
        history = self.fit(
            _MfTriples(train_users, train_items, self.n_items, batch_size, seed), epochs=epochs, verbose=0
        )
        for epoch, loss in enumerate(history.history["loss"], 1):
            logger.info("BPR-MF epoch %d: loss %.4f", epoch, loss)
        return self

    def score(self, users: np.ndarray, history: np.ndarray, weights: np.ndarray) -> np.ndarray:
        with inference_mode():
            p = self.user_vectors(users)
            return ops.convert_to_numpy(ops.matmul(p, ops.transpose(self.item_vectors.embeddings)))

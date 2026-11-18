"""Training of the hybrid model (Keras: model.fit) with early stopping, evaluation against the baselines,
bundle export.

One training step (TrainingModel): a batch of positive (user, book) pairs, each with random negative books;
loss = L_BPR + β·L_align + λ‖Θ‖² (CLAUDE.md, "Навчання").
"""

import logging
import time
from collections.abc import Callable
from contextlib import nullcontext
from dataclasses import asdict, dataclass
from datetime import UTC, datetime
from pathlib import Path

import keras
import numpy as np
from keras import ops

from recsys.dataset.pipeline import load_export
from recsys.model.baselines import MatrixFactorization, PopularityScorer, TfidfScorer
from recsys.model.bundle import save_bundle
from recsys.model.data import PAD, EvalSet, TrainingData, build_training_data
from recsys.model.evaluate import evaluate, new_metrics
from recsys.model.network import HybridModel, ModelConfig
from recsys.model.text import DEFAULT_TEXT_MODEL, Encoder, embed_texts, sentence_transformer_encoder

logger = logging.getLogger(__name__)


@dataclass
class TrainConfig:
    """Defaults follow CLAUDE.md ("Навчання")."""

    val_fraction: float = 0.1
    test_fraction: float = 0.1
    batch_size: int = 1024  # triples
    negatives: int = 4  # per positive
    lr: float = 1e-3
    l2: float = 1e-6  # λ‖Θ‖² on the embeddings of the batch
    align_weight: float = 0.1  # β
    cold_dropout: float = 0.2  # DropoutNet: probability of treating a book as new
    metadata_dropout: float = (
        0.3  # probability of hiding a book's author / publisher / numbers (content tower)
    )
    n_min: int = 5
    latent_dim: int = 64
    content_dim: int = 64
    history_length: int = 50
    decay_half_life_days: float = 180.0
    max_epochs: int = 30
    patience: int = 3  # epochs without NDCG@10 improvement on validation
    eval_users: int = 3000  # validation users sampled per epoch
    test_users: int = 10000  # test users sampled for the final evaluation (None/0 = all)
    ks: tuple[int, ...] = (10, 20)
    text_model: str = DEFAULT_TEXT_MODEL
    baselines: bool = True
    mf_epochs: int = 10
    seed: int = 42


def train(
    export_dir: Path,
    out_dir: Path,
    config: TrainConfig,
    device: str = "auto",
    name: str | None = None,
    encode: Encoder | None = None,
    cache_dir: Path | None = None,
    on_epoch: Callable[[int, dict], None] | None = None,
) -> Path:
    """device: "auto" (Keras uses the GPU if the backend sees one), "cpu" or "cuda"."""
    keras.utils.set_random_seed(config.seed)
    with nullcontext() if device == "auto" else keras.device(device):
        return _train(export_dir, out_dir, config, device, name, encode, cache_dir, on_epoch)


def _train(
    export_dir: Path,
    out_dir: Path,
    config: TrainConfig,
    device: str,
    name: str | None,
    encode: Encoder | None,
    cache_dir: Path | None,
    on_epoch: Callable[[int, dict], None] | None,
) -> Path:
    logger.info("Keras %s, backend %s, device %s", keras.__version__, keras.backend.backend(), device)

    export = load_export(export_dir)
    data = build_training_data(
        export, config.val_fraction, config.test_fraction, config.history_length, config.decay_half_life_days
    )
    logger.info(
        "Books %d, train users %d, positives %d | val users %d, test users %d, cold-start users %d",
        len(data.features.book_ids), len(data.user_ids), len(data.train_users),
        len(data.val.users), len(data.test.users), len(data.cold_test.users),
    )  # fmt: skip

    text_device = "cpu" if device == "cpu" else ("cuda" if _gpu_available() else "cpu")
    encode = encode or sentence_transformer_encoder(config.text_model, text_device)
    cache_file = cache_dir / f"{config.text_model.replace('/', '__')}.npz" if cache_dir else None
    text = embed_texts(data.features.texts, encode, cache_file)

    model = build_model(data, text, config)
    history = fit(model, data, config, on_epoch)

    metrics = {"hybrid": final_metrics(model.score_catalog, data, text, config)}
    if config.baselines:
        metrics |= baseline_metrics(data, text, config)
    metrics["training_curve"] = history
    weights = ops.convert_to_numpy(model.alpha_weights)
    metrics["alpha"] = {
        "w1_user": float(weights[0]),
        "w2_book": float(weights[1]),
        "bias": float(ops.convert_to_numpy(model.alpha_bias)),
        "gamma": float(ops.convert_to_numpy(model.gamma)),
    }

    name = name or f"hybrid-{datetime.now(UTC):%Y%m%d-%H%M}"
    bundle = save_bundle(
        out_dir / name,
        name=name,
        model=model,
        features=data.features,
        user_ids=data.user_ids,
        text_model=config.text_model,
        text_embeddings=text,
        metrics=metrics,
        training={
            "config": asdict(config),
            "val_start": data.split.val_start,
            "test_start": data.split.test_start,
            "export": export.meta,
            "framework": f"keras {keras.__version__} ({keras.backend.backend()})",
            "device": device,
        },
    )
    logger.info("Bundle written to %s", bundle)
    return bundle


def _gpu_available() -> bool:
    try:
        import torch

        return torch.cuda.is_available()
    except ImportError:
        return False


def build_model(data: TrainingData, text: np.ndarray, config: TrainConfig) -> HybridModel:
    features = data.features
    model = HybridModel(
        ModelConfig(
            n_items=len(features.book_ids),
            n_users=len(data.user_ids) + 1,
            text_dim=text.shape[1],
            n_authors=len(features.author_vocab),
            n_genres=len(features.genre_vocab),
            n_publishers=len(features.publisher_vocab),
            n_languages=len(features.language_vocab),
            numeric_dim=features.numeric.shape[1],
            author_slots=features.authors.shape[1],
            genre_slots=features.genres.shape[1],
            content_dim=config.content_dim,
            latent_dim=config.latent_dim,
            n_min=config.n_min,
        ),
        seed=config.seed,
    )
    model.load_book_features(features, text, data.item_counts)
    model.load_user_counts(data.user_counts)
    return model


class TripleBatches(keras.utils.PyDataset):
    """Training batches for model.fit: positives with sampled negatives and the users' histories.

    Per batch of positive (user, book) pairs:
    - `negatives` random books per positive (a random book is occasionally one the user likes: accepted);
    - the user's history without the positive itself (leave-one-out: the answer must not be in the profile);
    - the DropoutNet mask: each candidate is treated as a new book with probability `cold_dropout`.
    The positives are reshuffled every epoch.
    """

    def __init__(self, data: TrainingData, config: TrainConfig) -> None:
        super().__init__()
        self.users, self.items = data.train_users, data.train_items
        self.hist_items, self.hist_weights = data.train_histories.items, data.train_histories.weights
        self.n_items = len(data.features.book_ids)
        self.config = config
        self.rng = np.random.default_rng(config.seed)
        self.order = self.rng.permutation(len(self.users))

    def __len__(self) -> int:
        return -(-len(self.users) // self.config.batch_size)

    def __getitem__(self, index: int):
        size = self.config.batch_size
        batch = self.order[index * size : (index + 1) * size]
        users, positives = self.users[batch], self.items[batch]
        negatives = self.rng.integers(0, self.n_items, (len(batch), self.config.negatives))
        candidates = np.concatenate([positives[:, None], negatives], axis=1)  # column 0: the positive

        history = self.hist_items[users]
        history = np.where(history == positives[:, None], PAD, history)
        inputs = {
            "users": users,
            "candidates": candidates,
            "history": history,
            "weights": self.hist_weights[users],
            "force_cold": self.rng.random(candidates.shape) < self.config.cold_dropout,
            "align_items": np.unique(candidates),
        }
        return inputs, np.zeros(len(batch), dtype=np.float32)  # no targets: the positive is column 0

    def on_epoch_end(self) -> None:
        self.order = self.rng.permutation(len(self.users))


class TrainingModel(keras.Model):
    """Wraps HybridModel for model.fit(): call() scores a batch, compute_loss() returns the training loss.
    Only the wrapped model is saved; this class exists for training."""

    def __init__(self, model: HybridModel, config: TrainConfig) -> None:
        super().__init__()
        self.hybrid = model
        self.config = config
        self.bpr_tracker = keras.metrics.Mean(name="bpr")
        self.align_tracker = keras.metrics.Mean(name="align")

    def call(self, inputs, training=False):
        model = self.hybrid
        # c_i of the whole catalog, recomputed every step: the layers producing it are being trained.
        content = model.content(metadata_dropout=self.config.metadata_dropout if training else 0.0)
        profile = model.user_profile(content, inputs["history"], inputs["weights"])
        users, candidates = inputs["users"], inputs["candidates"]
        scores = model.score(users, candidates, profile, content, ops.cast(inputs["force_cold"], "bool"))

        # λ‖Θ‖² on the embedding rows this batch uses, per positive.
        embedded = [
            model.user_gmf(users),
            model.user_mlp(users),
            model.item_gmf(candidates),
            model.item_mlp(candidates),
        ]
        l2 = sum(ops.sum(ops.square(e)) for e in embedded) / ops.cast(ops.shape(users)[0], "float32")
        return {"z": scores.z, "align": model.align_loss(content, inputs["align_items"]), "l2": l2}

    def compute_loss(self, x=None, y=None, y_pred=None, sample_weight=None, training=True):
        z = y_pred["z"]
        # BPR: the positive (column 0) should score higher than each negative.
        bpr = -ops.mean(ops.log_sigmoid(z[:, :1] - z[:, 1:]))
        self.bpr_tracker.update_state(bpr)
        self.align_tracker.update_state(y_pred["align"])
        return bpr + self.config.align_weight * y_pred["align"] + self.config.l2 * y_pred["l2"]

    @property
    def metrics(self):
        return [*super().metrics, self.bpr_tracker, self.align_tracker]


class ValidationCallback(keras.callbacks.Callback):
    """After every epoch: NDCG@k on validation users (full-catalog ranking), the training curve, the best
    weights and early stopping. The equivalent of EarlyStopping(restore_best_weights=True) on a metric that
    Keras can't compute itself."""

    def __init__(
        self,
        model: HybridModel,
        data: TrainingData,
        config: TrainConfig,
        on_epoch: Callable[[int, dict], None] | None,
    ) -> None:
        super().__init__()
        self.hybrid, self.data, self.config, self.on_epoch = model, data, config, on_epoch
        self.k = min(config.ks)
        self.history: list[dict] = []
        self.best_score, self.best_weights, self.stale = -1.0, None, 0
        self.started = time.monotonic()

    def on_epoch_begin(self, epoch: int, logs=None) -> None:
        self.started = time.monotonic()

    def on_epoch_end(self, epoch: int, logs=None) -> None:
        logs = logs or {}
        k = self.k
        val = self._evaluate(self.data.val)
        # Logged, not used for early stopping (the spec stops on warm NDCG@10): shows the cold-start effect.
        cold_val = self._evaluate(self.data.cold_val) if len(self.data.cold_val.users) else {f"ndcg@{k}": 0.0}
        record = {
            "loss": float(logs.get("loss", 0.0)),
            "bpr": float(logs.get("bpr", 0.0)),
            "align": float(logs.get("align", 0.0)),
            "epoch": epoch + 1,
            "seconds": round(time.monotonic() - self.started, 1),
            f"val_ndcg@{k}": val[f"ndcg@{k}"],
            f"val_recall@{k}": val[f"recall@{k}"],
            f"cold_val_ndcg@{k}": cold_val[f"ndcg@{k}"],
        }
        self.history.append(record)
        logger.info(
            "Epoch %d: %s",
            epoch + 1,
            {n: round(v, 4) if isinstance(v, float) else v for n, v in record.items()},
        )
        if self.on_epoch:
            self.on_epoch(epoch + 1, record)

        if val[f"ndcg@{k}"] > self.best_score:
            self.best_score, self.stale = val[f"ndcg@{k}"], 0
            self.best_weights = self.hybrid.get_weights()
        else:
            self.stale += 1
            if self.stale >= self.config.patience:
                logger.info("Early stopping: no improvement for %d epochs", self.stale)
                self.model.stop_training = True

    def on_train_end(self, logs=None) -> None:
        if self.best_weights is not None:
            self.hybrid.set_weights(self.best_weights)

    def _evaluate(self, eval_set: EvalSet) -> dict:
        metrics = _metrics(self.data, self.config, ops.convert_to_numpy(self.hybrid.text))
        return evaluate(self.hybrid.score_catalog, eval_set, metrics, self.config.eval_users)


def fit(
    model: HybridModel,
    data: TrainingData,
    config: TrainConfig,
    on_epoch: Callable[[int, dict], None] | None = None,
) -> list[dict]:
    """Adam on L = L_BPR + β·L_align + λ‖Θ‖²; leaves the model with the best weights by validation NDCG@10
    and returns the training curve."""
    trainer = TrainingModel(model, config)
    trainer.compile(optimizer=keras.optimizers.Adam(config.lr))
    validation = ValidationCallback(model, data, config, on_epoch)
    trainer.fit(TripleBatches(data, config), epochs=config.max_epochs, callbacks=[validation], verbose=0)
    return validation.history


def final_metrics(scorer, data: TrainingData, text: np.ndarray, config: TrainConfig) -> dict:
    max_users = config.test_users or None
    return {
        "test": evaluate(scorer, data.test, _metrics(data, config, text), max_users, seed=config.seed),
        "cold_start": evaluate(scorer, data.cold_test, _metrics(data, config, text), seed=config.seed),
    }


def baseline_metrics(data: TrainingData, text: np.ndarray, config: TrainConfig) -> dict:
    logger.info("Evaluating baselines")
    mf = MatrixFactorization(
        len(data.user_ids) + 1, len(data.features.book_ids), config.latent_dim, seed=config.seed
    )
    mf.fit_triples(data.train_users, data.train_items, config.mf_epochs, seed=config.seed)
    return {
        "popularity": final_metrics(PopularityScorer(data.item_counts), data, text, config),
        "tfidf": final_metrics(TfidfScorer(data.features.texts), data, text, config),
        "matrix_factorization": final_metrics(mf.score, data, text, config),
    }


def _metrics(data: TrainingData, config: TrainConfig, text: np.ndarray):
    return new_metrics(config.ks, len(data.features.book_ids), text, data.item_counts, len(data.user_ids))

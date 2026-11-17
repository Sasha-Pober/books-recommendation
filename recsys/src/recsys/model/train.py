"""Training of the hybrid model with early stopping, evaluation against the baselines, bundle export."""

import logging
import time
from collections.abc import Callable
from dataclasses import asdict, dataclass
from datetime import UTC, datetime
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F

from recsys.dataset.pipeline import load_export
from recsys.model.baselines import MatrixFactorization, PopularityScorer, TfidfScorer
from recsys.model.bundle import save_bundle
from recsys.model.data import PAD, TrainingData, build_training_data
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
    torch.manual_seed(config.seed)
    np.random.seed(config.seed)
    target_device = torch.device(
        ("cuda" if torch.cuda.is_available() else "cpu") if device == "auto" else device
    )
    logger.info("Device: %s", target_device)

    export = load_export(export_dir)
    data = build_training_data(
        export, config.val_fraction, config.test_fraction, config.history_length, config.decay_half_life_days
    )
    logger.info(
        "Books %d, train users %d, positives %d | val users %d, test users %d, cold-start users %d",
        len(data.features.book_ids), len(data.user_ids), len(data.train_users),
        len(data.val.users), len(data.test.users), len(data.cold_test.users),
    )  # fmt: skip

    encode = encode or sentence_transformer_encoder(config.text_model, str(target_device))
    cache_file = cache_dir / f"{config.text_model.replace('/', '__')}.npz" if cache_dir else None
    text = embed_texts(data.features.texts, encode, cache_file)

    model = build_model(data, text, config).to(target_device)
    best_state, history = fit(model, data, config, target_device, on_epoch)
    model.load_state_dict(best_state)
    model.eval()

    metrics = {"hybrid": final_metrics(model.score_catalog, data, text, config, target_device)}
    if config.baselines:
        metrics |= baseline_metrics(data, text, config, target_device)
    metrics["training_curve"] = history
    metrics["alpha"] = {
        "w1_user": model.alpha_weights[0].item(),
        "w2_book": model.alpha_weights[1].item(),
        "bias": model.alpha_bias.item(),
        "gamma": model.gamma.item(),
    }

    name = name or f"hybrid-{datetime.now(UTC):%Y%m%d-%H%M}"
    bundle = save_bundle(
        out_dir / name,
        name=name,
        model=model.cpu(),
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
            "device": str(target_device),
        },
    )
    logger.info("Bundle written to %s", bundle)
    return bundle


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
        )
    )
    model.load_book_features(features, text, data.item_counts)
    model.load_user_counts(data.user_counts)
    return model


def fit(
    model: HybridModel,
    data: TrainingData,
    config: TrainConfig,
    device: torch.device,
    on_epoch: Callable[[int, dict], None] | None = None,
) -> tuple[dict, list[dict]]:
    """Adam on L = L_BPR + β·L_align + λ‖Θ‖²; returns the best weights by validation NDCG@10."""
    optimizer = torch.optim.Adam(model.parameters(), lr=config.lr)
    users = torch.tensor(data.train_users, device=device)
    items = torch.tensor(data.train_items, device=device)
    hist_items = torch.as_tensor(data.train_histories.items, device=device)
    hist_weights = torch.as_tensor(data.train_histories.weights, device=device)
    n_items = len(data.features.book_ids)
    generator = torch.Generator(device=device).manual_seed(config.seed)
    k = min(config.ks)

    best_score, best_state, stale, history = -1.0, None, 0, []
    for epoch in range(1, config.max_epochs + 1):
        model.train()
        started, totals, steps = time.monotonic(), {"loss": 0.0, "bpr": 0.0, "align": 0.0}, 0
        order = torch.randperm(len(users), device=device, generator=generator)
        for start in range(0, len(order), config.batch_size):
            batch = order[start : start + config.batch_size]
            u, pos = users[batch], items[batch]
            neg = torch.randint(
                0, n_items, (len(batch), config.negatives), device=device, generator=generator
            )
            candidates = torch.cat([pos.unsqueeze(1), neg], dim=1)

            # Leave-one-out profile: the positive itself is not part of the user's history.
            user_history = hist_items[u]
            user_history = torch.where(
                user_history == pos.unsqueeze(1), torch.full_like(user_history, PAD), user_history
            )
            content = model.content(metadata_dropout=config.metadata_dropout)
            profile = model.user_profile(content, user_history, hist_weights[u])
            force_cold = (
                torch.rand(candidates.shape, device=device, generator=generator) < config.cold_dropout
            )

            scores = model.score(u, candidates, profile, content, force_cold)
            bpr = -F.logsigmoid(scores.z[:, :1] - scores.z[:, 1:]).mean()
            align = model.align_loss(content, candidates.flatten().unique())
            embedded = [
                model.user_gmf(u),
                model.user_mlp(u),
                model.item_gmf(candidates),
                model.item_mlp(candidates),
            ]
            l2 = sum(e.pow(2).sum() for e in embedded) / len(batch)
            loss = bpr + config.align_weight * align + config.l2 * l2

            optimizer.zero_grad()
            loss.backward()
            optimizer.step()
            totals["loss"] += loss.item()
            totals["bpr"] += bpr.item()
            totals["align"] += align.item()
            steps += 1

        model.eval()
        val = evaluate(
            model.score_catalog,
            data.val,
            _metrics(data, config, model.text.cpu().numpy()),
            device,
            config.eval_users,
        )
        # Logged, not used for early stopping (the spec stops on warm NDCG@10): shows the cold-start effect.
        cold_val = (
            evaluate(
                model.score_catalog,
                data.cold_val,
                _metrics(data, config, model.text.cpu().numpy()),
                device,
                config.eval_users,
            )
            if len(data.cold_val.users)
            else {f"ndcg@{k}": 0.0}
        )
        record = {k_: v / steps for k_, v in totals.items()} | {
            "epoch": epoch,
            "seconds": round(time.monotonic() - started, 1),
            f"val_ndcg@{k}": val[f"ndcg@{k}"],
            f"val_recall@{k}": val[f"recall@{k}"],
            f"cold_val_ndcg@{k}": cold_val[f"ndcg@{k}"],
        }
        history.append(record)
        logger.info(
            "Epoch %d: %s",
            epoch,
            {k_: round(v, 4) if isinstance(v, float) else v for k_, v in record.items()},
        )
        if on_epoch:
            on_epoch(epoch, record)

        if val[f"ndcg@{k}"] > best_score:
            best_score, stale = val[f"ndcg@{k}"], 0
            best_state = {name: t.detach().clone() for name, t in model.state_dict().items()}
        else:
            stale += 1
            if stale >= config.patience:
                logger.info("Early stopping: no improvement for %d epochs", stale)
                break

    assert best_state is not None
    return best_state, history


def final_metrics(
    scorer, data: TrainingData, text: np.ndarray, config: TrainConfig, device: torch.device
) -> dict:
    max_users = config.test_users or None
    return {
        "test": evaluate(
            scorer, data.test, _metrics(data, config, text), device, max_users, seed=config.seed
        ),
        "cold_start": evaluate(
            scorer, data.cold_test, _metrics(data, config, text), device, seed=config.seed
        ),
    }


def baseline_metrics(data: TrainingData, text: np.ndarray, config: TrainConfig, device: torch.device) -> dict:
    logger.info("Evaluating baselines")
    mf = MatrixFactorization(len(data.user_ids) + 1, len(data.features.book_ids), config.latent_dim)
    mf.fit(data.train_users, data.train_items, config.mf_epochs, device, seed=config.seed).eval()
    return {
        "popularity": final_metrics(PopularityScorer(data.item_counts, device), data, text, config, device),
        "tfidf": final_metrics(TfidfScorer(data.features.texts), data, text, config, device),
        "matrix_factorization": final_metrics(mf.score, data, text, config, device),
    }


def _metrics(data: TrainingData, config: TrainConfig, text: np.ndarray):
    return new_metrics(config.ks, len(data.features.book_ids), text, data.item_counts, len(data.user_ids))

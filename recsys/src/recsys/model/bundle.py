"""The trained model as a folder, produced by training (e.g. on Kaggle) and read by `recsys import-model`."""

import json
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path

import numpy as np

from recsys.dataset.features import BookFeatures
from recsys.model.network import HybridModel, ModelConfig

# 2: Keras weights (model.weights.h5). Format 1 held PyTorch weights (model.pt).
BUNDLE_FORMAT = 2
WEIGHTS_FILE = "model.weights.h5"


@dataclass
class Bundle:
    name: str
    created_at: datetime
    model: HybridModel
    book_ids: list[str]  # model item index → book id
    user_ids: list[str]  # model user index i+1 → user id (0 = unknown user)
    vocabularies: dict[str, list[str]]  # authors, genres, publishers, languages (index i+1 → key)
    numeric_stats: dict[str, float]
    text_model: str
    text_embeddings: np.ndarray  # float32 [books, 384], L2-normalized
    metrics: dict
    training: dict  # hyperparameters, split dates, export metadata


def save_bundle(
    folder: Path,
    *,
    name: str,
    model: HybridModel,
    features: BookFeatures,
    user_ids: list[str],
    text_model: str,
    text_embeddings: np.ndarray,
    metrics: dict,
    training: dict,
) -> Path:
    folder.mkdir(parents=True, exist_ok=False)
    (folder / "bundle.json").write_text(
        json.dumps(
            {
                "format": BUNDLE_FORMAT,
                "name": name,
                "created_at": datetime.now(UTC).isoformat(),
                "text_model": text_model,
                "model_config": model.config.to_dict(),
            },
            indent=2,
        )
    )
    model.save_weights(folder / WEIGHTS_FILE)
    np.save(folder / "text_embeddings.npy", text_embeddings.astype(np.float32))
    (folder / "ids.json").write_text(json.dumps({"books": features.book_ids, "users": user_ids}))
    (folder / "vocabularies.json").write_text(
        json.dumps(
            {
                "authors": features.author_vocab.keys,
                "genres": features.genre_vocab.keys,
                "publishers": features.publisher_vocab.keys,
                "languages": features.language_vocab.keys,
                "numeric_stats": features.numeric_stats,
            }
        )
    )
    (folder / "metrics.json").write_text(json.dumps(metrics, indent=2))
    (folder / "training.json").write_text(json.dumps(training, indent=2, default=str))
    return folder


def load_bundle(folder: Path) -> Bundle:
    header = json.loads((folder / "bundle.json").read_text())
    if header.get("format") != BUNDLE_FORMAT:
        raise ValueError(
            f"Unsupported bundle format {header.get('format')} (expected {BUNDLE_FORMAT}); retrain the model."
        )
    config = header["model_config"]
    config["mlp_dims"] = tuple(config["mlp_dims"])
    model = HybridModel(ModelConfig(**config))  # all weights are created in the constructor
    model.load_weights(folder / WEIGHTS_FILE)

    ids = json.loads((folder / "ids.json").read_text())
    vocab = json.loads((folder / "vocabularies.json").read_text())
    return Bundle(
        name=header["name"],
        created_at=datetime.fromisoformat(header["created_at"]),
        model=model,
        book_ids=ids["books"],
        user_ids=ids["users"],
        vocabularies={k: vocab[k] for k in ("authors", "genres", "publishers", "languages")},
        numeric_stats=vocab["numeric_stats"],
        text_model=header["text_model"],
        text_embeddings=np.load(folder / "text_embeddings.npy"),
        metrics=json.loads((folder / "metrics.json").read_text()),
        training=json.loads((folder / "training.json").read_text()),
    )

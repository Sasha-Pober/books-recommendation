"""Frozen sentence-transformers text embeddings of books, cached per text."""

import hashlib
import logging
from collections.abc import Callable, Sequence
from pathlib import Path

import numpy as np

logger = logging.getLogger(__name__)

DEFAULT_TEXT_MODEL = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"  # mean pooling, 384-d

Encoder = Callable[[list[str]], np.ndarray]


def sentence_transformer_encoder(model_name: str, device: str, batch_size: int = 128) -> Encoder:
    from sentence_transformers import SentenceTransformer

    model = SentenceTransformer(model_name, device=device)

    def encode(texts: list[str]) -> np.ndarray:
        return model.encode(
            texts,
            batch_size=batch_size,
            normalize_embeddings=True,
            convert_to_numpy=True,
            show_progress_bar=True,
        )

    return encode


def embed_texts(texts: Sequence[str], encode: Encoder, cache_file: Path | None = None) -> np.ndarray:
    """L2-normalized embeddings [len(texts), dim]. Only texts missing from the cache are encoded."""
    keys = np.array([hashlib.sha1(t.encode()).hexdigest() for t in texts])
    cached: dict[str, np.ndarray] = {}
    if cache_file is not None and cache_file.exists():
        with np.load(cache_file) as stored:
            cached = dict(zip(stored["keys"], stored["vectors"], strict=True))

    missing = sorted({k for k in keys if k not in cached})
    if missing:
        logger.info("Encoding %d texts (%d cached)", len(missing), len(keys) - len(missing))
        text_of = dict(zip(keys, texts, strict=True))
        vectors = np.asarray(encode([text_of[k] for k in missing]), dtype=np.float32)
        cached.update(zip(missing, vectors, strict=True))
        if cache_file is not None:
            cache_file.parent.mkdir(parents=True, exist_ok=True)
            all_keys = np.array(sorted(cached))
            np.savez(cache_file, keys=all_keys, vectors=np.stack([cached[k] for k in all_keys]))

    return np.stack([cached[k] for k in keys]).astype(np.float32)

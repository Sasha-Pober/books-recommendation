"""A small synthetic export: two kinds of books (sea / war poetry), readers who prefer one kind."""

import json
from pathlib import Path

import numpy as np
import pandas as pd

from recsys.dataset.export import FORMAT_VERSION

TEXT_DIM = 16


def book_id(i: int) -> str:
    return f"00000000-0000-4000-8000-{i:012d}"


def user_id(u: int) -> str:
    """Even readers like sea poetry, odd ones war poetry."""
    return f"00000000-0000-4000-9000-{u:012d}"


def user_number(user: str) -> int:
    return int(user[-12:])


def write_export(folder: Path, n_users: int = 120, n_books: int = 60, seed: int = 0) -> Path:
    rng = np.random.default_rng(seed)
    start = pd.Timestamp("2016-01-01", tz="UTC")
    kinds = np.array(["sea", "war"])[np.arange(n_books) % 2]
    books = pd.DataFrame(
        {
            "id": [book_id(i) for i in range(n_books)],
            "title": [f"{kind} poems {i}" for i, kind in enumerate(kinds)],
            "annotation": [f"Poems about the {kind}." for kind in kinds],
            "language": "en",
            "year": rng.integers(1950, 2015, n_books),
            "pages": rng.integers(40, 300, n_books),
            "publisher_id": [f"p{i % 3}" for i in range(n_books)],
            "stock": 25,
            # The last 6 books appear late: they become the cold-start books.
            "created_at": [start + pd.Timedelta(days=0 if i < n_books - 6 else 330) for i in range(n_books)],
        }
    )
    rows = []
    for u in range(n_users):
        likes = "sea" if u % 2 == 0 else "war"
        liked = [i for i in range(n_books - 6) if kinds[i] == likes]
        for i in rng.choice(liked, size=12, replace=False):
            rows.append((user_id(u), book_id(i), 1.0, start + pd.Timedelta(days=int(rng.integers(0, 300)))))
        for i in rng.choice(np.arange(n_books - 6), size=2, replace=False):  # noise
            rows.append((user_id(u), book_id(i), 0.5, start + pd.Timedelta(days=int(rng.integers(0, 300)))))
        late = [i for i in range(n_books - 6, n_books) if kinds[i] == likes]
        rows.append(
            (
                user_id(u),
                book_id(int(rng.choice(late))),
                1.0,
                start + pd.Timedelta(days=int(rng.integers(340, 360))),
            )
        )
    interactions = pd.DataFrame(rows, columns=["user_id", "book_id", "weight", "created_at"]).assign(
        event_type="Purchase"
    )

    folder.mkdir(parents=True, exist_ok=True)
    interactions.to_parquet(folder / "interactions.parquet")
    books.to_parquet(folder / "books.parquet")
    pd.DataFrame({"book_id": books["id"], "author_id": [f"a{i % 5}" for i in range(n_books)]}).to_parquet(
        folder / "book_authors.parquet"
    )
    pd.DataFrame({"book_id": books["id"], "genre_id": kinds}).to_parquet(folder / "book_genres.parquet")
    (folder / "meta.json").write_text(json.dumps({"format_version": FORMAT_VERSION}))
    return folder


def fake_encoder(texts: list[str]) -> np.ndarray:
    """Deterministic 'text embeddings' that separate the two kinds of books."""
    vectors = np.zeros((len(texts), TEXT_DIM), dtype=np.float32)
    for row, text in enumerate(texts):
        vectors[row, 0 if "sea" in text else 1] = 1.0
        vectors[row, 2 + hash(text) % (TEXT_DIM - 2)] = 0.1
    return vectors / np.linalg.norm(vectors, axis=1, keepdims=True)

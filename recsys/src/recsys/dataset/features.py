"""Book metadata → model inputs. Index 0 is reserved for padding / unknown in every vocabulary."""

from dataclasses import dataclass

import numpy as np
import pandas as pd


@dataclass
class Vocabulary:
    """Stable mapping of string keys (ids, language codes) to indices 1..n."""

    keys: list[str]

    def __post_init__(self) -> None:
        self._index = {key: i + 1 for i, key in enumerate(self.keys)}

    def __len__(self) -> int:
        return len(self.keys) + 1  # including padding

    def index(self, key: object) -> int:
        return self._index.get(str(key), 0) if key is not None and not pd.isna(key) else 0

    @classmethod
    def build(cls, values: pd.Series) -> "Vocabulary":
        return cls(sorted({str(v) for v in values.dropna()}))


@dataclass
class BookFeatures:
    book_ids: list[str]  # row i of every array describes book_ids[i]
    texts: list[str]  # title + annotation, for the sentence-transformers text embedding
    authors: np.ndarray  # int64 [books, max_authors], padded with 0
    genres: np.ndarray  # int64 [books, max_genres], padded with 0
    publisher: np.ndarray  # int64 [books]
    language: np.ndarray  # int64 [books]
    numeric: np.ndarray  # float32 [books, 4]: year z-score, log(pages) z-score, year missing, pages missing
    author_vocab: Vocabulary
    genre_vocab: Vocabulary
    publisher_vocab: Vocabulary
    language_vocab: Vocabulary
    numeric_stats: dict[str, float]  # means and stds, so new books are encoded the same way at inference


def book_text(title: str, annotation: object) -> str:
    return f"{title}. {annotation}" if isinstance(annotation, str) and annotation.strip() else title


def encode_books(
    books: pd.DataFrame,
    book_authors: pd.DataFrame,
    book_genres: pd.DataFrame,
    max_authors: int = 3,
    max_genres: int = 4,
    vocabularies: dict[str, list[str]] | None = None,
    numeric_stats: dict[str, float] | None = None,
) -> BookFeatures:
    """Encodes books with vocabularies and statistics built from them, or (at inference, for books added
    after training) with the trained model's `vocabularies` and `numeric_stats`, so the encoding matches
    training."""
    books = books.sort_values("id").reset_index(drop=True)
    book_ids = books["id"].astype(str).tolist()
    row = {book_id: i for i, book_id in enumerate(book_ids)}

    if vocabularies is not None:
        author_vocab, genre_vocab, publisher_vocab, language_vocab = (
            Vocabulary(vocabularies[name]) for name in ("authors", "genres", "publishers", "languages")
        )
    else:
        author_vocab = Vocabulary.build(book_authors["author_id"])
        genre_vocab = Vocabulary.build(book_genres["genre_id"])
        publisher_vocab = Vocabulary.build(books["publisher_id"])
        language_vocab = Vocabulary.build(books["language"])

    authors = _padded(book_authors, "author_id", author_vocab, row, max_authors)
    genres = _padded(book_genres, "genre_id", genre_vocab, row, max_genres)

    years = pd.to_numeric(books["year"], errors="coerce")
    log_pages = np.log1p(pd.to_numeric(books["pages"], errors="coerce"))
    stats = numeric_stats or {
        "year_mean": float(years.mean()) if years.notna().any() else 0.0,
        "year_std": float(years.std()) if years.notna().sum() > 1 else 1.0,
        "log_pages_mean": float(log_pages.mean()) if log_pages.notna().any() else 0.0,
        "log_pages_std": float(log_pages.std()) if log_pages.notna().sum() > 1 else 1.0,
    }
    numeric = np.stack(
        [
            ((years - stats["year_mean"]) / (stats["year_std"] or 1.0)).fillna(0).to_numpy(),
            ((log_pages - stats["log_pages_mean"]) / (stats["log_pages_std"] or 1.0)).fillna(0).to_numpy(),
            years.isna().astype(float).to_numpy(),
            log_pages.isna().astype(float).to_numpy(),
        ],
        axis=1,
    ).astype(np.float32)

    return BookFeatures(
        book_ids=book_ids,
        texts=[book_text(t, a) for t, a in zip(books["title"], books["annotation"], strict=True)],
        authors=authors,
        genres=genres,
        publisher=np.array([publisher_vocab.index(p) for p in books["publisher_id"]], dtype=np.int64),
        language=np.array([language_vocab.index(lang) for lang in books["language"]], dtype=np.int64),
        numeric=numeric,
        author_vocab=author_vocab,
        genre_vocab=genre_vocab,
        publisher_vocab=publisher_vocab,
        language_vocab=language_vocab,
        numeric_stats=stats,
    )


def _padded(
    links: pd.DataFrame, column: str, vocab: Vocabulary, row: dict[str, int], width: int
) -> np.ndarray:
    result = np.zeros((len(row), width), dtype=np.int64)
    filled = np.zeros(len(row), dtype=np.int64)
    for book_id, key in zip(links["book_id"].astype(str), links[column], strict=True):
        i = row.get(book_id)
        if i is not None and filled[i] < width:
            result[i, filled[i]] = vocab.index(key)
            filled[i] += 1
    return result

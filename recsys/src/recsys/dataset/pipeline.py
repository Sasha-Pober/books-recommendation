"""From exported events to training data: interest per (user, book) and the time-based split."""

import json
from dataclasses import dataclass
from pathlib import Path

import pandas as pd

from recsys.dataset.export import FORMAT_VERSION


@dataclass
class ExportedData:
    interactions: pd.DataFrame  # user_id, book_id, event_type, weight, created_at
    books: pd.DataFrame  # id, title, annotation, language, year, pages, publisher_id, stock, created_at
    book_authors: pd.DataFrame  # book_id, author_id
    book_genres: pd.DataFrame  # book_id, genre_id
    meta: dict


def load_export(folder: Path) -> ExportedData:
    meta = json.loads((folder / "meta.json").read_text())
    if meta.get("format_version") != FORMAT_VERSION:
        raise ValueError(
            f"Unsupported export format {meta.get('format_version')} (expected {FORMAT_VERSION})."
        )
    tables = {
        name: pd.read_parquet(folder / f"{name}.parquet")
        for name in ("interactions", "books", "book_authors", "book_genres")
    }
    return ExportedData(**tables, meta=meta)


def build_pairs(interactions: pd.DataFrame) -> pd.DataFrame:
    """Interest of each user in each book: r = the maximum weight of their events for that pair; t = the time
    the pair first reached that weight. Only pairs with r > 0 are returned (a 1-3 star rating alone is not
    a positive signal)."""
    events = interactions[["user_id", "book_id", "weight", "created_at"]].sort_values(
        ["user_id", "book_id", "weight", "created_at"], ascending=[True, True, False, True]
    )
    pairs = events.drop_duplicates(["user_id", "book_id"], keep="first")
    pairs = pairs.rename(columns={"weight": "r", "created_at": "t"})
    return pairs[pairs["r"] > 0].sort_values("t").reset_index(drop=True)


@dataclass
class Split:
    """Pairs split by time. val/test are "warm": users and books that occur in train. cold_test holds pairs
    of known users with books that first appeared in the test period (the cold-start evaluation)."""

    train: pd.DataFrame
    val: pd.DataFrame
    test: pd.DataFrame
    cold_test: pd.DataFrame
    val_start: pd.Timestamp
    test_start: pd.Timestamp


def time_split(pairs: pd.DataFrame, val_fraction: float = 0.1, test_fraction: float = 0.1) -> Split:
    if not (val_fraction > 0 and test_fraction > 0 and val_fraction + test_fraction < 1):
        raise ValueError("val_fraction and test_fraction must be positive and sum to less than 1.")

    val_start = pairs["t"].quantile(1 - val_fraction - test_fraction)
    test_start = pairs["t"].quantile(1 - test_fraction)
    train = pairs[pairs["t"] < val_start]
    val = pairs[(pairs["t"] >= val_start) & (pairs["t"] < test_start)]
    test = pairs[pairs["t"] >= test_start]

    train_users, train_books = set(train["user_id"]), set(train["book_id"])
    first_seen = pairs.groupby("book_id")["t"].min()
    new_in_test = set(first_seen[first_seen >= test_start].index)

    def warm(frame: pd.DataFrame) -> pd.DataFrame:
        return frame[frame["user_id"].isin(train_users) & frame["book_id"].isin(train_books)].reset_index(
            drop=True
        )

    cold_test = test[test["user_id"].isin(train_users) & test["book_id"].isin(new_in_test)]
    return Split(
        train=train.reset_index(drop=True),
        val=warm(val),
        test=warm(test),
        cold_test=cold_test.reset_index(drop=True),
        val_start=val_start,
        test_start=test_start,
    )

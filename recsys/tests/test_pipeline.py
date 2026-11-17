import json
from pathlib import Path

import numpy as np
import pandas as pd
import pytest

from recsys.dataset.export import FORMAT_VERSION
from recsys.dataset.features import encode_books
from recsys.dataset.pipeline import build_pairs, load_export, time_split


def at(day: int) -> pd.Timestamp:
    return pd.Timestamp("2020-01-01", tz="UTC") + pd.Timedelta(days=day)


def events(*rows: tuple[str, str, float, int]) -> pd.DataFrame:
    return pd.DataFrame(
        [
            {"user_id": u, "book_id": b, "weight": w, "created_at": at(d), "event_type": "x"}
            for u, b, w, d in rows
        ]
    )


def test_build_pairs_takes_max_weight_and_the_time_it_was_first_reached() -> None:
    pairs = build_pairs(
        events(
            ("u", "a", 0.2, 1),  # view
            ("u", "a", 1.0, 5),  # purchase → r = 1.0 at day 5
            ("u", "a", 1.0, 9),  # 5-star rating later: same weight, earlier time wins
            ("u", "b", 0.0, 2),  # only a 2-star rating → not a positive
            ("u", "c", 0.5, 3),
        )
    )

    assert pairs[["book_id", "r", "t"]].to_dict("records") == [
        {"book_id": "c", "r": 0.5, "t": at(3)},
        {"book_id": "a", "r": 1.0, "t": at(5)},
    ]


def test_time_split_keeps_warm_pairs_and_collects_cold_start_books() -> None:
    rows = [(u, f"old{d % 3}", 1.0, d) for d in range(80) for u in ("u1", "u2")]  # train period
    rows += [("u1", "old0", 1.0, 85), ("u1", "old1", 1.0, 95)]  # warm val / test
    rows += [("stranger", "old2", 1.0, 96)]  # unknown user → not evaluated
    rows += [("u2", "new", 1.0, 97)]  # a book that first appears in the test period
    pairs = pd.DataFrame(
        [{"user_id": u, "book_id": b, "r": r, "t": at(d)} for u, b, r, d in rows]
    ).drop_duplicates(["user_id", "book_id", "t"])

    split = time_split(pairs, val_fraction=0.025, test_fraction=0.025)

    assert split.train["t"].max() < split.val_start <= split.test_start
    assert set(split.test["user_id"]) <= {"u1", "u2"}
    assert "new" not in set(split.test["book_id"])
    assert split.cold_test[["user_id", "book_id"]].to_dict("records") == [{"user_id": "u2", "book_id": "new"}]
    assert split.cold_val.empty  # no book first appeared in the validation period


@pytest.mark.parametrize(("val", "test"), [(0, 0.1), (0.5, 0.5), (-0.1, 0.1)])
def test_time_split_rejects_invalid_fractions(val: float, test: float) -> None:
    with pytest.raises(ValueError):
        time_split(pd.DataFrame({"t": [at(0)]}), val, test)


def test_encode_books() -> None:
    books = pd.DataFrame(
        {
            "id": ["b2", "b1", "b3"],
            "title": ["Two", "One", "Three"],
            "annotation": [None, "About the sea.", "  "],
            "language": ["en", "uk", None],
            "year": [2000, 1990, None],
            "pages": [100, None, 300],
            "publisher_id": ["p1", None, "p1"],
        }
    )
    authors = pd.DataFrame(
        {"book_id": ["b1", "b1", "b1", "b1", "b2"], "author_id": ["a1", "a2", "a3", "a4", "a1"]}
    )
    genres = pd.DataFrame({"book_id": ["b2"], "genre_id": ["g1"]})

    features = encode_books(books, authors, genres, max_authors=3, max_genres=2)

    assert features.book_ids == ["b1", "b2", "b3"]
    assert features.texts == ["One. About the sea.", "Two", "Three"]
    assert features.authors.tolist() == [[1, 2, 3], [1, 0, 0], [0, 0, 0]]  # capped at 3, padded with 0
    assert features.genres.tolist() == [[0, 0], [1, 0], [0, 0]]
    assert features.publisher.tolist() == [0, 1, 1]
    assert features.language.tolist() == [2, 1, 0]  # vocabulary: en=1, uk=2; missing=0
    np.testing.assert_allclose(features.numeric[:, 0], [-np.sqrt(0.5), np.sqrt(0.5), 0], atol=1e-6)
    assert features.numeric[:, 2].tolist() == [0, 0, 1]  # year missing flag
    assert features.numeric[:, 3].tolist() == [1, 0, 0]  # pages missing flag
    assert features.numeric.dtype == np.float32


def test_load_export_checks_the_format_version(tmp_path: Path) -> None:
    for name in ("interactions", "books", "book_authors", "book_genres"):
        pd.DataFrame({"x": [1]}).to_parquet(tmp_path / f"{name}.parquet")
    (tmp_path / "meta.json").write_text(json.dumps({"format_version": FORMAT_VERSION}))

    assert load_export(tmp_path).meta["format_version"] == FORMAT_VERSION

    (tmp_path / "meta.json").write_text(json.dumps({"format_version": 999}))
    with pytest.raises(ValueError):
        load_export(tmp_path)

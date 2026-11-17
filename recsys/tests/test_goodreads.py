import gzip
import json
from pathlib import Path

import pandas as pd
import pytest

from recsys import events
from recsys.goodreads.transform import (
    build_import,
    normalize_language,
    parse_times,
    select_genres,
    synthetic_price,
    to_events,
)
from recsys.ids import stable_id


def write_gz(path: Path, records: list[dict]) -> None:
    with gzip.open(path, "wt") as file:
        file.writelines(json.dumps(r) + "\n" for r in records)


def shelf(
    user: str,
    book: str,
    *,
    read: bool = True,
    rating: int = 0,
    added: str = "Wed May 09 09:33:44 -0700 2007",
    updated: str | None = None,
) -> dict:
    return {
        "user_id": user,
        "book_id": book,
        "is_read": read,
        "rating": rating,
        "date_added": added,
        "date_updated": updated or added,
        "read_at": "",
        "started_at": "",
    }


def shelves_frame(records: list[dict]) -> pd.DataFrame:
    frame = pd.DataFrame(records).rename(
        columns={"user_id": "user", "book_id": "book", "date_added": "added", "date_updated": "updated"}
    )
    frame["added"], frame["updated"] = parse_times(frame["added"]), parse_times(frame["updated"])
    return frame


def test_parse_times_converts_goodreads_format_to_utc() -> None:
    times = parse_times(pd.Series(["Wed May 09 09:33:44 -0700 2007", "", "garbage"]))

    assert times[0] == pd.Timestamp("2007-05-09 16:33:44", tz="UTC")
    assert times[1:].isna().all()


def test_to_events_maps_shelves_to_store_events() -> None:
    shelves = shelves_frame(
        [
            shelf("u", "to-read", read=False),
            shelf("u", "loved", rating=5, updated="Thu May 10 10:00:00 +0000 2007"),
            shelf("u", "disliked", rating=2),
        ]
    )

    result = to_events(shelves)

    as_tuples = set(result[["book", "event_type", "weight"]].itertuples(index=False, name=None))
    assert as_tuples == {
        ("to-read", events.ADD_TO_WISHLIST, 0.5),
        ("loved", events.PURCHASE, 1.0),
        ("loved", events.RATING, 1.0),
        ("disliked", events.PURCHASE, 1.0),
        ("disliked", events.RATING, 0.0),
    }
    rating_time = result.loc[
        (result["book"] == "loved") & (result["event_type"] == events.RATING), "created_at"
    ]
    assert rating_time.item() == pd.Timestamp("2007-05-10 10:00:00", tz="UTC")  # rated at date_updated


@pytest.mark.parametrize(
    ("counts", "expected"),
    [
        ({"poetry": 31, "fiction": 8, "romance": 2}, ["Poetry", "Fiction"]),  # 2 < 10% of 31
        ({"poetry": 3, "children": 1}, ["Poetry"]),  # at least 2 shelvings
        ({"poetry": "5", "unknown, label": "5"}, ["Unknown, Label", "Poetry"]),  # counts as strings
        ({}, []),
    ],
)
def test_select_genres(counts: dict, expected: list[str]) -> None:
    assert select_genres(counts) == expected


@pytest.mark.parametrize(
    ("code", "expected"),
    [("eng", "en"), ("en-US", "en"), ("ukr", "uk"), ("de", "de"), ("xyz", None), ("", None)],
)
def test_normalize_language(code: str, expected: str | None) -> None:
    assert normalize_language(code) == expected


def test_synthetic_price_is_stable_and_plausible() -> None:
    prices = [synthetic_price(str(i)) for i in range(500)]

    assert synthetic_price("42") == synthetic_price("42")
    assert all(150 <= p <= 600 and p % 10 == 0 for p in prices)
    assert len(set(prices)) > 20


def test_build_import(tmp_path: Path) -> None:
    write_gz(
        tmp_path / "goodreads_interactions_poetry.json.gz",
        [
            # Reader "a" shelved 3 known books + 1 unknown → kept with min_user_books=3.
            shelf("a", "1", added="Mon Jan 01 10:00:00 +0000 2007"),
            shelf("a", "2", read=False, added="Tue Jan 02 10:00:00 +0000 2007"),
            shelf("a", "3", rating=4, added="Wed Jan 03 10:00:00 +0000 2007"),
            shelf("a", "missing-book"),
            # Reader "b" shelved only 2 books → dropped.
            shelf("b", "1"),
            shelf("b", "2"),
        ],
    )
    book = {"language_code": "eng", "publisher": "Faber", "description": "  Poems\n about  sea. ",
            "num_pages": "80", "publication_year": "1887", "image_url": "https://x/1.jpg",
            "title_without_series": "Book", "isbn13": "9780000000001", "isbn": ""}  # fmt: skip
    write_gz(
        tmp_path / "goodreads_books_poetry.json.gz",
        [
            book
            | {
                "book_id": "1",
                "authors": [{"author_id": "t1", "role": "Translator"}, {"author_id": "p1", "role": ""}],
            },
            book
            | {
                "book_id": "2",
                "authors": [{"author_id": "p1", "role": ""}],
                "image_url": "https://x/nophoto.png",
            },
            book | {"book_id": "3", "authors": [{"author_id": "p2", "role": ""}], "isbn13": "9780000000001"},
            book | {"book_id": "99", "authors": []},  # nobody kept shelved it
        ],
    )
    write_gz(
        tmp_path / "goodreads_book_authors.json.gz",
        [{"author_id": "p1", "name": "Ліна Костенко"}, {"author_id": "p2", "name": "Poet Two"},
         {"author_id": "t1", "name": "Translator"}],
    )  # fmt: skip
    write_gz(
        tmp_path / "goodreads_book_genres_initial.json.gz",
        [
            {"book_id": "1", "genres": {"poetry": 10, "fiction": 3}},
            {"book_id": "50", "genres": {"poetry": 9}},
        ],
    )

    data = build_import(tmp_path, "poetry", min_user_books=3)

    book_id = {g: stable_id("goodreads:book", g) for g in ("1", "2", "3")}
    assert set(data.books["goodreads_id"]) == {"1", "2", "3"}
    assert data.users["goodreads_id"].tolist() == ["a"]

    first = data.books.set_index("goodreads_id").loc["1"]
    assert first["annotation"] == "Poems about sea."
    assert (first["language"], first["year"], first["pages"]) == ("en", 1887, 80)
    assert first["created_at"] == pd.Timestamp("2007-01-01 10:00:00", tz="UTC")  # first event
    books = data.books.set_index("goodreads_id")
    assert pd.isna(books.loc["2", "cover_image_url"])
    assert pd.isna(books.loc["3", "isbn"])  # duplicate ISBN of book 1

    # Translators aren't linked as authors.
    assert set(data.book_authors.itertuples(index=False, name=None)) == {
        (book_id["1"], stable_id("goodreads:author", "p1")),
        (book_id["2"], stable_id("goodreads:author", "p1")),
        (book_id["3"], stable_id("goodreads:author", "p2")),
    }
    assert data.book_genres.to_dict("records") == [
        {"book_id": book_id["1"], "genre_name": "Poetry"},
        {"book_id": book_id["1"], "genre_name": "Fiction"},
    ]

    assert len(data.interactions) == 4  # 2 purchases + 1 wishlist + 1 rating, no event for the unknown book
    assert data.interactions["user_id"].eq(stable_id("goodreads:user", "a")).all()
    assert data.ratings[["book_id", "score"]].to_dict("records") == [{"book_id": book_id["3"], "score": 4}]


def test_stable_ids_are_deterministic_per_kind() -> None:
    assert stable_id("goodreads:book", "1") == stable_id("goodreads:book", "1")
    assert stable_id("goodreads:book", "1") != stable_id("goodreads:user", "1")

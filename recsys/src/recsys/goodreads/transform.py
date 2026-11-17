"""Pure transformations of the Goodreads files into store records (no database access)."""

import gzip
import hashlib
import math
import re
from collections.abc import Iterator
from dataclasses import dataclass
from pathlib import Path

import orjson
import pandas as pd

from recsys import events
from recsys.ids import stable_id

IMPORT_SESSION_ID = "goodreads-import"

TITLE_MAX, NAME_MAX, ISBN_MAX, URL_MAX = 300, 200, 20, 2048

# The fuzzy genre labels of goodreads_book_genres_initial, as store genre names.
GENRE_NAMES = {
    "children": "Children",
    "comics, graphic": "Comics & Graphic",
    "fantasy, paranormal": "Fantasy & Paranormal",
    "fiction": "Fiction",
    "history, historical fiction, biography": "History & Biography",
    "mystery, thriller, crime": "Mystery & Thriller",
    "non-fiction": "Non-fiction",
    "poetry": "Poetry",
    "romance": "Romance",
    "young-adult": "Young Adult",
}

# Goodreads language codes → ISO 639-1 as used by the store.
LANGUAGES = {
    "eng": "en", "en-us": "en", "en-gb": "en", "en-ca": "en", "en-au": "en", "en-in": "en",
    "spa": "es", "es-mx": "es", "fre": "fr", "fra": "fr", "ger": "de", "deu": "de", "ita": "it",
    "por": "pt", "pt-br": "pt", "rus": "ru", "ukr": "uk", "pol": "pl", "jpn": "ja", "ara": "ar",
    "per": "fa", "fas": "fa", "tur": "tr", "dut": "nl", "nld": "nl", "swe": "sv", "nor": "no",
    "dan": "da", "fin": "fi", "gre": "el", "ell": "el", "heb": "he", "hin": "hi", "zho": "zh",
    "chi": "zh", "kor": "ko", "ind": "id", "cze": "cs", "ces": "cs", "rum": "ro", "ron": "ro",
    "hun": "hu", "bul": "bg", "srp": "sr", "hrv": "hr", "lit": "lt", "lav": "lv", "est": "et",
}  # fmt: skip

_GOODREADS_TIME_FORMAT = "%a %b %d %H:%M:%S %z %Y"  # "Wed May 09 09:33:44 -0700 2007"


def read_json_lines(path: Path) -> Iterator[dict]:
    with gzip.open(path, "rb") as file:
        for line in file:
            yield orjson.loads(line)


def parse_times(values: pd.Series) -> pd.Series:
    """Goodreads timestamps → UTC; unparseable or empty values become NaT."""
    return pd.to_datetime(values, format=_GOODREADS_TIME_FORMAT, utc=True, errors="coerce")


def load_interactions(path: Path) -> pd.DataFrame:
    """One row per (user, book) shelf record."""
    columns: dict[str, list] = {k: [] for k in ("user", "book", "is_read", "rating", "added", "updated")}
    for record in read_json_lines(path):
        columns["user"].append(record["user_id"])
        columns["book"].append(record["book_id"])
        columns["is_read"].append(bool(record.get("is_read")))
        columns["rating"].append(int(record.get("rating") or 0))
        columns["added"].append(record.get("date_added") or "")
        columns["updated"].append(record.get("date_updated") or "")
    frame = pd.DataFrame(columns)
    frame["added"] = parse_times(frame["added"])
    frame["updated"] = parse_times(frame["updated"]).fillna(frame["added"])
    return frame.dropna(subset=["added"])


def to_events(shelves: pd.DataFrame) -> pd.DataFrame:
    """Shelf records → store events:
    - shelved, not read ("to-read") → AddToWishlist at date_added;
    - read → Purchase (strongest implicit signal) at date_added;
    - rated 1-5 → Rating at date_updated, weight 1.0 for 4-5 stars, 0 otherwise."""
    wishlist = shelves.loc[~shelves["is_read"], ["user", "book", "added"]].assign(
        event_type=events.ADD_TO_WISHLIST, weight=events.WEIGHTS[events.ADD_TO_WISHLIST]
    )
    purchase = shelves.loc[shelves["is_read"], ["user", "book", "added"]].assign(
        event_type=events.PURCHASE, weight=events.WEIGHTS[events.PURCHASE]
    )
    rated = shelves.loc[shelves["rating"] > 0]
    rating = (
        rated[["user", "book", "updated"]]
        .rename(columns={"updated": "added"})
        .assign(event_type=events.RATING, weight=rated["rating"].map(events.rating_weight))
    )
    result = pd.concat([wishlist, purchase, rating], ignore_index=True)
    return result.rename(columns={"added": "created_at"})[
        ["user", "book", "event_type", "weight", "created_at"]
    ]


def to_ratings(shelves: pd.DataFrame) -> pd.DataFrame:
    """The latest rating per (user, book)."""
    rated = shelves.loc[shelves["rating"] > 0].sort_values("updated")
    rated = rated.drop_duplicates(["user", "book"], keep="last")
    return rated.rename(columns={"rating": "score", "updated": "at"})[["user", "book", "score", "at"]]


def normalize_language(code: str | None) -> str | None:
    if not code:
        return None
    code = code.strip().lower()
    if code in LANGUAGES:
        return LANGUAGES[code]
    return code if len(code) == 2 and code.isalpha() else None


def select_genres(counts: dict[str, int | str], max_genres: int = 4) -> list[str]:
    """Store genre names for a book from its fuzzy shelf counts: genres with at least 10% of the top count
    (and at least 2 shelvings), most frequent first."""
    if not counts:
        return []
    parsed = {label: int(count) for label, count in counts.items()}
    threshold = max(2, math.ceil(0.1 * max(parsed.values())))
    ranked = sorted((c, label) for label, c in parsed.items() if c >= threshold)
    return [GENRE_NAMES.get(label, label.title()) for _, label in reversed(ranked)][:max_genres]


def synthetic_price(goodreads_book_id: str) -> float:
    """The dataset has no prices: a stable pseudo-random price of 150-600 UAH in steps of 10."""
    digest = hashlib.sha256(goodreads_book_id.encode()).digest()
    return 150.0 + (int.from_bytes(digest[:4], "big") % 46) * 10


def _int_in(value: str | None, low: int, high: int) -> int | None:
    try:
        number = int(value) if value not in (None, "") else None
    except ValueError:
        return None
    return number if number is not None and low <= number <= high else None


def _clean_text(value: str | None, limit: int | None = None) -> str | None:
    if not value:
        return None
    text = re.sub(r"\s+", " ", value).strip()
    return (text[:limit] if limit else text) or None


def book_row(record: dict) -> dict:
    """A store book from a Goodreads book record (author ids are kept for linking)."""
    authors = record.get("authors") or []
    primary = [a["author_id"] for a in authors if not a.get("role")] or [a["author_id"] for a in authors[:1]]
    cover = record.get("image_url") or ""
    return {
        "goodreads_id": record["book_id"],
        "title": _clean_text(record.get("title_without_series") or record.get("title"), TITLE_MAX)
        or "Untitled",
        "isbn": _clean_text(record.get("isbn13") or record.get("isbn"), ISBN_MAX),
        "annotation": _clean_text(record.get("description")),
        "publisher_name": _clean_text(record.get("publisher"), NAME_MAX),
        "language": normalize_language(record.get("language_code")),
        "year": _int_in(record.get("publication_year"), 1, 2100),
        "pages": _int_in(record.get("num_pages"), 1, 100_000),
        "cover_image_url": None if "nophoto" in cover or len(cover) > URL_MAX else cover or None,
        "author_goodreads_ids": primary,
    }


@dataclass
class ImportData:
    """Store records ready to be written (ids are deterministic)."""

    users: pd.DataFrame  # id, goodreads_id, created_at
    authors: pd.DataFrame  # id, name
    books: (
        pd.DataFrame
    )  # id, title, isbn, annotation, publisher_name, language, year, pages, cover..., created_at
    book_authors: pd.DataFrame  # book_id, author_id
    book_genres: pd.DataFrame  # book_id, genre_name
    interactions: pd.DataFrame  # user_id, book_id, event_type, weight, created_at
    ratings: pd.DataFrame  # user_id, book_id, score, created_at, updated_at


def build_import(data_dir: Path, genre: str, min_user_books: int) -> ImportData:
    shelves = load_interactions(data_dir / f"goodreads_interactions_{genre}.json.gz")

    # Readers with very few shelved books add noise and size, but little signal.
    books_per_user = shelves.groupby("user")["book"].nunique()
    shelves = shelves[shelves["user"].isin(books_per_user[books_per_user >= min_user_books].index)]

    wanted_books = set(shelves["book"])
    books = pd.DataFrame(
        [
            book_row(r)
            for r in read_json_lines(data_dir / f"goodreads_books_{genre}.json.gz")
            if r["book_id"] in wanted_books
        ]
    )
    shelves = shelves[shelves["book"].isin(set(books["goodreads_id"]))]
    event_rows = to_events(shelves)

    books["id"] = books["goodreads_id"].map(lambda g: stable_id("goodreads:book", g))
    books["price"] = books["goodreads_id"].map(synthetic_price)
    books["stock"] = 25
    # A book "arrives" in the store with its first event, which keeps the cold-start split meaningful.
    first_seen = event_rows.groupby("book")["created_at"].min()
    books["created_at"] = books["goodreads_id"].map(first_seen)
    books = books.dropna(subset=["created_at"])
    # ISBNs are unique in the store; editions occasionally share one.
    books.loc[books["isbn"].notna() & books.duplicated("isbn", keep="first"), "isbn"] = None

    author_links = books[["id", "author_goodreads_ids"]].explode("author_goodreads_ids").dropna()
    author_ids = set(author_links["author_goodreads_ids"])
    authors = pd.DataFrame(
        [
            {"goodreads_id": r["author_id"], "name": _clean_text(r.get("name"), NAME_MAX)}
            for r in read_json_lines(data_dir / "goodreads_book_authors.json.gz")
            if r["author_id"] in author_ids
        ]
    ).dropna(subset=["name"])
    authors["id"] = authors["goodreads_id"].map(lambda g: stable_id("goodreads:author", g))
    author_links = author_links[author_links["author_goodreads_ids"].isin(set(authors["goodreads_id"]))]
    book_authors = pd.DataFrame(
        {
            "book_id": author_links["id"].to_numpy(),
            "author_id": author_links["author_goodreads_ids"].map(lambda g: stable_id("goodreads:author", g)),
        }
    ).drop_duplicates()

    book_ids = dict(zip(books["goodreads_id"], books["id"], strict=True))
    genre_rows = [
        {"book_id": book_ids[r["book_id"]], "genre_name": name}
        for r in read_json_lines(data_dir / "goodreads_book_genres_initial.json.gz")
        if r["book_id"] in book_ids
        for name in select_genres(r.get("genres") or {})
    ]
    book_genres = pd.DataFrame(genre_rows, columns=["book_id", "genre_name"])

    user_first_seen = event_rows.groupby("user")["created_at"].min()
    users = pd.DataFrame({"goodreads_id": user_first_seen.index, "created_at": user_first_seen.to_numpy()})
    users["id"] = users["goodreads_id"].map(lambda g: stable_id("goodreads:user", g))
    user_ids = dict(zip(users["goodreads_id"], users["id"], strict=True))

    interactions = pd.DataFrame(
        {
            "user_id": event_rows["user"].map(user_ids),
            "book_id": event_rows["book"].map(book_ids),
            "event_type": event_rows["event_type"],
            "weight": event_rows["weight"],
            "created_at": event_rows["created_at"],
        }
    ).dropna(subset=["book_id"])

    rated = to_ratings(shelves)
    ratings = pd.DataFrame(
        {
            "user_id": rated["user"].map(user_ids),
            "book_id": rated["book"].map(book_ids),
            "score": rated["score"],
            "created_at": rated["at"],
            "updated_at": rated["at"],
        }
    ).dropna(subset=["book_id"])

    return ImportData(
        users=users,
        authors=authors[["id", "name"]],
        books=books.drop(columns=["author_goodreads_ids"]),
        book_authors=book_authors,
        book_genres=book_genres,
        interactions=interactions,
        ratings=ratings,
    )

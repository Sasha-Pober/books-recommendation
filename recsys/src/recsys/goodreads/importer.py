"""Writes a Goodreads subset into the store database (catalog, readers, events, ratings)."""

import logging
from datetime import UTC, datetime
from pathlib import Path

import psycopg

from recsys.db_copy import copy_frame
from recsys.goodreads.transform import IMPORT_SESSION_ID, ImportData, build_import
from recsys.ids import stable_id

logger = logging.getLogger(__name__)


def import_goodreads(database_url: str, data_dir: Path, genre: str, min_user_books: int) -> None:
    logger.info("Reading the %s subset from %s", genre, data_dir)
    data = build_import(data_dir, genre, min_user_books)
    logger.info(
        "Prepared %d books, %d authors, %d readers, %d events, %d ratings",
        len(data.books), len(data.authors), len(data.users), len(data.interactions), len(data.ratings),
    )  # fmt: skip

    with psycopg.connect(database_url) as connection:  # one transaction: all or nothing
        write_import(connection, data)
    logger.info("Import committed")


def write_import(connection: psycopg.Connection, data: ImportData) -> None:
    now = datetime.now(UTC)
    with connection.cursor() as cur:
        # Reference data, matched by unique name: existing genres / publishers are reused.
        names = [("publisher", n) for n in data.books["publisher_name"].dropna().unique()]
        names += [("genre", n) for n in data.book_genres["genre_name"].unique()]
        for kind, table in (("publisher", "publishers"), ("genre", "genres")):
            rows = [(stable_id(f"goodreads:{kind}", n), n, now) for k, n in names if k == kind]
            cur.executemany(
                f"INSERT INTO {table} (id, name, created_at) VALUES (%s, %s, %s)"
                " ON CONFLICT (name) DO NOTHING",
                rows,
            )

        authors = data.authors.assign(created_at=now)
        _upsert(cur, "authors", authors, ["id", "full_name", "created_at"], rename={"name": "full_name"})

        cur.execute(
            """CREATE TEMP TABLE import_books (
                id uuid, title text, isbn text, annotation text, publisher_name text, language text,
                year int, pages int, cover_image_url text, price numeric, stock int, created_at timestamptz
            ) ON COMMIT DROP"""
        )
        book_columns = [
            "id", "title", "isbn", "annotation", "publisher_name", "language",
            "year", "pages", "cover_image_url", "price", "stock", "created_at",
        ]  # fmt: skip
        copy_frame(cur, "import_books", data.books.astype({"year": "Int64", "pages": "Int64"}), book_columns)
        cur.execute(
            """INSERT INTO books (id, title, isbn, annotation, publisher_id, language, year, pages,
                                  cover_image_url, price, stock, created_at)
               SELECT b.id, b.title,
                      -- the store's ISBNs are unique: drop one that another book already has
                      CASE WHEN EXISTS (SELECT 1 FROM books x WHERE x.isbn = b.isbn AND x.id <> b.id)
                           THEN NULL ELSE b.isbn END,
                      b.annotation, p.id, b.language, b.year, b.pages, b.cover_image_url, b.price, b.stock,
                      b.created_at
               FROM import_books b LEFT JOIN publishers p ON p.name = b.publisher_name
               ON CONFLICT (id) DO NOTHING"""
        )

        _upsert(cur, "book_authors", data.book_authors, ["book_id", "author_id"])
        cur.execute("CREATE TEMP TABLE import_book_genres (book_id uuid, genre_name text) ON COMMIT DROP")
        copy_frame(cur, "import_book_genres", data.book_genres, ["book_id", "genre_name"])
        cur.execute(
            """INSERT INTO book_genres (book_id, genre_id)
               SELECT t.book_id, g.id FROM import_book_genres t JOIN genres g ON g.name = t.genre_name
               ON CONFLICT DO NOTHING"""
        )

        # Readers: real accounts without a password (they can't sign in), in the Customer role.
        users = data.users.assign(
            email=data.users["goodreads_id"] + "@goodreads.import",
            full_name="Goodreads reader " + data.users["goodreads_id"].str[:8],
            security_stamp=data.users["id"].astype(str),
            concurrency_stamp=data.users["id"].astype(str),
            email_confirmed=True,
            phone_number_confirmed=False,
            two_factor_enabled=False,
            lockout_enabled=True,
            access_failed_count=0,
        )
        users["user_name"] = users["email"]
        users["normalized_user_name"] = users["normalized_email"] = users["email"].str.upper()
        _upsert(
            cur,
            "users",
            users,
            [
                "id", "full_name", "created_at", "user_name", "normalized_user_name", "email",
                "normalized_email", "email_confirmed", "security_stamp", "concurrency_stamp",
                "phone_number_confirmed", "two_factor_enabled", "lockout_enabled", "access_failed_count",
            ],
        )  # fmt: skip
        cur.execute("CREATE TEMP TABLE import_user_ids (id uuid) ON COMMIT DROP")
        copy_frame(cur, "import_user_ids", data.users, ["id"])
        cur.execute(
            """INSERT INTO user_roles (user_id, role_id)
               SELECT u.id, r.id FROM import_user_ids u CROSS JOIN roles r
               WHERE r.normalized_name = 'CUSTOMER'
               ON CONFLICT DO NOTHING"""
        )

        # Events: replace the previous import's events (they have no natural key).
        cur.execute("DELETE FROM interactions WHERE session_id = %s", [IMPORT_SESSION_ID])
        copy_frame(
            cur,
            "interactions",
            data.interactions.assign(session_id=IMPORT_SESSION_ID),
            ["user_id", "session_id", "book_id", "event_type", "weight", "created_at"],
        )

        cur.execute(
            """CREATE TEMP TABLE import_ratings (
                user_id uuid, book_id uuid, score int, created_at timestamptz, updated_at timestamptz
            ) ON COMMIT DROP"""
        )
        copy_frame(
            cur, "import_ratings", data.ratings, ["user_id", "book_id", "score", "created_at", "updated_at"]
        )
        cur.execute(
            """INSERT INTO ratings (user_id, book_id, score, created_at, updated_at)
               SELECT user_id, book_id, score, created_at, updated_at FROM import_ratings
               ON CONFLICT (user_id, book_id) DO UPDATE
                   SET score = excluded.score, updated_at = excluded.updated_at"""
        )


def _upsert(
    cur: psycopg.Cursor, table: str, frame, columns: list[str], rename: dict[str, str] | None = None
) -> None:
    """COPY into a temporary copy of the table, then insert rows whose key doesn't exist yet."""
    staging = f"import_{table}"
    cur.execute(f"CREATE TEMP TABLE {staging} (LIKE {table} INCLUDING DEFAULTS) ON COMMIT DROP")
    copy_frame(cur, staging, frame.rename(columns=rename or {}), columns)
    cols = ", ".join(columns)
    cur.execute(f"INSERT INTO {table} ({cols}) SELECT {cols} FROM {staging} ON CONFLICT DO NOTHING")

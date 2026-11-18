"""The store database as the recommender's data source (catalog and user histories)."""

from collections.abc import Callable
from contextlib import AbstractContextManager
from uuid import UUID

import pandas as pd
import psycopg

from recsys.db_copy import copy_query
from recsys.inference import CatalogData

Connect = Callable[[], AbstractContextManager[psycopg.Connection]]

# Recently added books ranked by the weight of their recent events. "Recent" is measured back from the newest
# event rather than from now, so it also works on imported historical data.
_POPULAR_NEW = """
    WITH newest AS (
        SELECT id, created_at FROM books WHERE stock > 0 ORDER BY created_at DESC LIMIT %(pool)s
    ), latest AS (
        SELECT max(created_at) AS t FROM interactions
    )
    SELECT n.id
    FROM newest n
    LEFT JOIN interactions i
        ON i.book_id = n.id AND i.created_at >= (SELECT t FROM latest) - make_interval(days => %(days)s)
    GROUP BY n.id, n.created_at
    ORDER BY coalesce(sum(i.weight), 0) DESC, n.created_at DESC
    LIMIT %(count)s
"""


class DatabaseCatalog:
    def __init__(
        self, connect: Connect, popular_pool: int = 500, popular_days: int = 90, popular_count: int = 100
    ):
        self._connect = connect
        self._popular = {"pool": popular_pool, "days": popular_days, "count": popular_count}

    def load(self) -> CatalogData:
        with self._connect() as connection, connection.cursor() as cursor:
            books = copy_query(
                cursor,
                "SELECT id, title, annotation, language, year, pages, publisher_id, stock, created_at"
                " FROM books",
            )
            books["created_at"] = pd.to_datetime(books["created_at"], utc=True, format="ISO8601")
            book_authors = copy_query(cursor, "SELECT book_id, author_id FROM book_authors")
            book_genres = copy_query(cursor, "SELECT book_id, genre_id FROM book_genres")
            popular_new = [str(row[0]) for row in cursor.execute(_POPULAR_NEW, self._popular).fetchall()]
        return CatalogData(books, book_authors, book_genres, popular_new)


class DatabaseHistory:
    def __init__(self, connect: Connect) -> None:
        self._connect = connect

    def recent_events(self, user_id: UUID | None, session_id: str, limit: int) -> pd.DataFrame:
        # Index-backed: interactions (user_id, created_at) and (session_id).
        if user_id is not None:
            query = "SELECT book_id, weight, created_at FROM interactions WHERE user_id = %s"
            key: object = user_id
        else:
            query = (
                "SELECT book_id, weight, created_at FROM interactions"
                " WHERE user_id IS NULL AND session_id = %s"
            )
            key = session_id
        with self._connect() as connection:
            rows = connection.execute(f"{query} ORDER BY created_at DESC LIMIT %s", [key, limit]).fetchall()
        return pd.DataFrame(rows, columns=["book_id", "weight", "created_at"])

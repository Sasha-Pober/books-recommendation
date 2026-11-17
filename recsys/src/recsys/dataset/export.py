"""Exports the training data from the store database to parquet files (e.g. for a Kaggle dataset)."""

import json
import logging
from datetime import UTC, datetime
from pathlib import Path

import pandas as pd
import psycopg

from recsys import events
from recsys.db_copy import copy_query

logger = logging.getLogger(__name__)

FORMAT_VERSION = 1

# Signed-in users' events only: guests have no stable identity to train on.
_QUERIES = {
    "interactions": """SELECT user_id, book_id, event_type, weight, created_at FROM interactions
                       WHERE user_id IS NOT NULL ORDER BY created_at, id""",
    "books": """SELECT id, title, annotation, language, year, pages, publisher_id, stock, created_at
                FROM books ORDER BY id""",
    "book_authors": "SELECT book_id, author_id FROM book_authors ORDER BY book_id, author_id",
    "book_genres": "SELECT book_id, genre_id FROM book_genres ORDER BY book_id, genre_id",
    "genres": "SELECT id, name, parent_id FROM genres ORDER BY id",
}

_TIME_COLUMNS = {"interactions": ["created_at"], "books": ["created_at"]}


def export_dataset(database_url: str, out_dir: Path) -> Path:
    """Writes <out_dir>/<UTC timestamp>/{table}.parquet + meta.json and returns that folder."""
    target = out_dir / datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ")
    target.mkdir(parents=True)

    counts: dict[str, int] = {}
    # One REPEATABLE READ snapshot: all tables are consistent with each other.
    with psycopg.connect(database_url) as connection, connection.cursor() as cur:
        cur.execute("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY")
        for name, query in _QUERIES.items():
            frame = copy_query(cur, query)
            for column in _TIME_COLUMNS.get(name, []):
                frame[column] = pd.to_datetime(frame[column], utc=True, format="ISO8601")
            frame.to_parquet(target / f"{name}.parquet", index=False)
            counts[name] = len(frame)
            logger.info("Exported %s: %d rows", name, len(frame))

    meta = {
        "format_version": FORMAT_VERSION,
        "exported_at": datetime.now(UTC).isoformat(),
        "counts": counts,
        "event_weights": events.WEIGHTS | {events.RATING: "1.0 if score >= 4 else 0.0"},
    }
    (target / "meta.json").write_text(json.dumps(meta, indent=2))
    logger.info("Dataset written to %s", target)
    return target

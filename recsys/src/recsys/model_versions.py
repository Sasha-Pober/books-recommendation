"""The model_versions table: which imported models exist and which one is active (single source of truth)."""

from dataclasses import dataclass
from datetime import datetime

import psycopg
from psycopg.types.json import Jsonb


@dataclass
class ModelVersion:
    id: int
    name: str
    trained_at: datetime
    is_active: bool
    metrics: dict | None


def get(connection: psycopg.Connection, version_id: int) -> ModelVersion | None:
    row = connection.execute(
        "SELECT id, name, trained_at, is_active, metrics_json FROM model_versions WHERE id = %s", [version_id]
    ).fetchone()
    return ModelVersion(*row) if row else None


def get_active(connection: psycopg.Connection) -> ModelVersion | None:
    row = connection.execute(
        "SELECT id, name, trained_at, is_active, metrics_json FROM model_versions WHERE is_active"
    ).fetchone()
    return ModelVersion(*row) if row else None


def insert(connection: psycopg.Connection, name: str, trained_at: datetime, metrics: dict) -> int:
    row = connection.execute(
        """INSERT INTO model_versions (name, trained_at, metrics_json, is_active)
           VALUES (%s, %s, %s, false) RETURNING id""",
        [name, trained_at, Jsonb(metrics)],
    ).fetchone()
    assert row is not None
    return row[0]


def set_active(connection: psycopg.Connection, version_id: int) -> None:
    """At most one active version (partial unique index): switch the old one off first, then the new one on,
    in the caller's transaction."""
    connection.execute(
        "UPDATE model_versions SET is_active = false WHERE is_active AND id <> %s", [version_id]
    )
    connection.execute("UPDATE model_versions SET is_active = true WHERE id = %s", [version_id])


def test_ndcg(metrics: dict | None) -> float | None:
    """The number models are compared by: test NDCG@10 of the hybrid model."""
    try:
        return float(metrics["hybrid"]["test"]["ndcg@10"])  # type: ignore[index]
    except (KeyError, TypeError, ValueError):
        return None

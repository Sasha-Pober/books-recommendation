"""`recsys import-model`: registers a trained bundle (from Kaggle or a local run) in the store."""

import logging
import shutil
import tempfile
import zipfile
from enum import StrEnum
from pathlib import Path

import numpy as np
import pandas as pd
import psycopg

from recsys import model_versions
from recsys.db_copy import copy_frame
from recsys.model.bundle import load_bundle

logger = logging.getLogger(__name__)


class Activation(StrEnum):
    AUTO = "auto"  # if there is no active model, or the new one has a higher test NDCG@10
    YES = "yes"
    NO = "no"


def import_model(database_url: str, source: Path, models_dir: Path, activation: Activation) -> int:
    """Copies the bundle to <models_dir>/<name>/, writes model_versions and book_embeddings, may activate it.
    Returns the new model_versions id."""
    with tempfile.TemporaryDirectory() as temporary:
        folder = _unpack(source, Path(temporary))
        bundle = load_bundle(folder)  # validates the format and that the weights load
        target = models_dir / bundle.name
        if target.exists():
            raise FileExistsError(f"{target} already exists: model '{bundle.name}' was imported before.")

        with psycopg.connect(database_url) as connection:
            if connection.execute("SELECT 1 FROM model_versions WHERE name = %s", [bundle.name]).fetchone():
                raise ValueError(f"Model '{bundle.name}' is already registered in model_versions.")

            known = {str(r[0]) for r in connection.execute("SELECT id FROM books").fetchall()}
            present = np.array([book_id in known for book_id in bundle.book_ids])
            missing = int((~present).sum())
            if missing:
                logger.warning(
                    "%d of %d books of the model are no longer in the store", missing, len(present)
                )
            new_books = len(known) - int(present.sum())
            if new_books:
                logger.info(
                    "%d store books are newer than the model (they are handled as cold start)", new_books
                )

            version_id = model_versions.insert(connection, bundle.name, bundle.created_at, bundle.metrics)
            _write_embeddings(
                connection, version_id, np.array(bundle.book_ids)[present], bundle.text_embeddings[present]
            )

            activate = _should_activate(connection, activation, bundle.metrics)
            # Files first, then commit: a registered version always has its files.
            models_dir.mkdir(parents=True, exist_ok=True)
            shutil.copytree(folder, target)
            try:
                if activate:
                    model_versions.set_active(connection, version_id)
                connection.commit()
            except Exception:
                shutil.rmtree(target, ignore_errors=True)
                raise

    logger.info(
        "Imported model '%s' as version %d (%s); test NDCG@10 %s",
        bundle.name, version_id, "active" if activate else "not active",
        model_versions.test_ndcg(bundle.metrics),
    )  # fmt: skip
    return version_id


def _unpack(source: Path, temporary: Path) -> Path:
    """A bundle folder, or a zip of one (as downloaded from Kaggle), possibly with one enclosing folder."""
    if source.is_dir():
        return source
    if not zipfile.is_zipfile(source):
        raise ValueError(f"{source} is neither a bundle folder nor a zip file.")
    with zipfile.ZipFile(source) as archive:
        for member in archive.namelist():  # refuse paths escaping the target folder
            if Path(member).is_absolute() or ".." in Path(member).parts:
                raise ValueError(f"Unsafe path in archive: {member}")
        archive.extractall(temporary)
    candidates = [p.parent for p in temporary.rglob("bundle.json")]
    if len(candidates) != 1:
        raise ValueError(f"Expected exactly one bundle in {source}, found {len(candidates)}.")
    return candidates[0]


def _should_activate(connection: psycopg.Connection, activation: Activation, metrics: dict) -> bool:
    if activation is not Activation.AUTO:
        return activation is Activation.YES
    active = model_versions.get_active(connection)
    if active is None:
        return True
    new, current = model_versions.test_ndcg(metrics), model_versions.test_ndcg(active.metrics)
    better = new is not None and (current is None or new > current)
    logger.info("Active model '%s' has test NDCG@10 %s; the new one %s", active.name, current, new)
    return better


def _write_embeddings(connection: psycopg.Connection, version_id: int, book_ids, vectors: np.ndarray) -> None:
    """book_embeddings(book_id, model_version_id, vector(384)) in pgvector's text format: [v1,v2,...]."""
    text = ["[" + ",".join(f"{v:.6g}" for v in row) + "]" for row in vectors]
    frame = pd.DataFrame({"book_id": book_ids, "model_version_id": version_id, "vector": text})
    with connection.cursor() as cursor:
        copy_frame(cursor, "book_embeddings", frame, ["book_id", "model_version_id", "vector"])

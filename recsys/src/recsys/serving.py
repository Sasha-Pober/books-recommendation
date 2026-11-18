"""Loading imported models into the running service and keeping them in sync with model_versions.is_active."""

import logging
import threading
from collections.abc import Callable
from contextlib import AbstractContextManager
from pathlib import Path
from typing import TYPE_CHECKING

import psycopg

from recsys import model_versions
from recsys.recommender import ModelRegistry, Recommender

if TYPE_CHECKING:
    from recsys.inference import CatalogSource, HistorySource, InferenceSettings

logger = logging.getLogger(__name__)

Connect = Callable[[], AbstractContextManager[psycopg.Connection]]


class ModelNotFound(Exception):
    """No such model version, or its files are not in the models folder."""


def load_model(
    version: model_versions.ModelVersion,
    models_dir: Path,
    catalog: "CatalogSource",
    history: "HistorySource",
    settings: "InferenceSettings | None" = None,
) -> Recommender:
    """Loads the bundle and the current catalog. Raises if either fails, so a broken model never replaces
    a working one."""
    folder = models_dir / version.name
    if not (folder / "bundle.json").exists():
        raise ModelNotFound(
            f"Files of model '{version.name}' (version {version.id}) are not in {models_dir}."
        )
    # Imported here: Keras, PyTorch and sentence-transformers are heavy and only needed once a model exists.
    from recsys.inference import HybridRecommender
    from recsys.model.bundle import load_bundle
    from recsys.model.text import sentence_transformer_encoder

    logger.info("Loading model '%s' (version %d)", version.name, version.id)
    bundle = load_bundle(folder)
    return HybridRecommender(
        version.id,
        version.name,
        bundle,
        catalog,
        history,
        # For books added after training: the same frozen text model; loaded on first need.
        encoder=lambda: sentence_transformer_encoder(bundle.text_model, "cpu"),
        settings=settings,
    )


class ModelLoader:
    """The active model is the one marked in model_versions. Activation through the API loads the model first
    and only then marks it active; activation elsewhere (recsys import-model) is picked up by polling."""

    def __init__(
        self,
        connect: Connect,
        registry: ModelRegistry,
        models_dir: Path,
        poll_seconds: float,
        load: Callable[[model_versions.ModelVersion, Path], Recommender],
    ) -> None:
        self._connect = connect
        self._registry = registry
        self._models_dir = models_dir
        self._poll_seconds = poll_seconds
        self._load = load
        self._lock = threading.Lock()  # one load / activation at a time
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    def activate(self, version_id: int) -> Recommender:
        with self._lock:
            with self._connect() as connection:
                version = model_versions.get(connection, version_id)
            if version is None:
                raise ModelNotFound(f"Model version {version_id} does not exist.")
            previous = self._registry.current
            already_loaded = previous is not None and previous.model_version_id == version_id
            loaded = previous if already_loaded else self._load(version, self._models_dir)

            self._registry.activate(loaded)
            try:
                with self._connect() as connection:
                    model_versions.set_active(connection, version_id)
                    connection.commit()
            except Exception:
                if previous is not None:
                    self._registry.activate(previous)
                raise
            logger.info("Activated model '%s' (version %d)", loaded.name, version_id)
            return loaded

    def sync(self) -> None:
        """Loads the model marked active in the database if it isn't the one in memory."""
        with self._lock:
            with self._connect() as connection:
                active = model_versions.get_active(connection)
            current = self._registry.current
            if active is None or (current is not None and current.model_version_id == active.id):
                return
            self._registry.activate(self._load(active, self._models_dir))
            logger.info("Now serving model '%s' (version %d)", active.name, active.id)

    def start(self) -> None:
        self._thread = threading.Thread(target=self._poll, name="model-poller", daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        if self._thread is not None:
            self._thread.join(timeout=5)

    def _poll(self) -> None:
        while True:
            try:
                self.sync()
            except Exception as error:  # the service keeps serving the current model
                logger.warning("Checking for a newly activated model failed: %s", error)
            try:
                if (current := self._registry.current) is not None:
                    current.refresh_if_stale()
            except Exception as error:  # the previous catalog snapshot stays in use
                logger.warning("Refreshing the catalog failed: %s", error)
            if self._stop.wait(self._poll_seconds):
                return

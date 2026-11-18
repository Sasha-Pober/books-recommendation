import os

# Before keras is imported anywhere (tests import it directly): the model runs on the PyTorch backend.
os.environ.setdefault("KERAS_BACKEND", "torch")

from collections.abc import Iterator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from recsys.config import Settings
from recsys.main import create_app

SERVICE_KEY = "test-service-key-0123456789"


@pytest.fixture
def settings(tmp_path) -> Settings:
    # Port 1: nothing listens there, so the database is reported unavailable quickly.
    return Settings(
        database_url="postgresql://user:pass@127.0.0.1:1/none",
        service_key=SERVICE_KEY,
        artifacts_dir=tmp_path,
        database_timeout=0.5,
    )


@pytest.fixture
def client(settings: Settings) -> Iterator[TestClient]:
    with TestClient(create_app(settings), headers={"X-Service-Key": SERVICE_KEY}) as test_client:
        yield test_client


@pytest.fixture(scope="session")
def trained(tmp_path_factory) -> tuple[Path, Path]:
    """A small hybrid model trained on the synthetic export: (export folder, bundle folder)."""
    from recsys.model.train import TrainConfig, train
    from tests.synthetic import fake_encoder, write_export

    folder = tmp_path_factory.mktemp("synthetic")
    export = write_export(folder / "export")
    config = TrainConfig(
        max_epochs=8, patience=8, batch_size=128, eval_users=500, test_users=0, mf_epochs=5, n_min=3
    )
    bundle = train(export, folder / "bundles", config, device="cpu", name="test", encode=fake_encoder)
    return export, bundle

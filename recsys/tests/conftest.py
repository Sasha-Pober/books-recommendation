from collections.abc import Iterator

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

from contextlib import contextmanager
from dataclasses import dataclass
from datetime import UTC, datetime
from pathlib import Path

import pytest

from recsys import model_versions
from recsys.model_versions import ModelVersion
from recsys.recommender import ModelRegistry
from recsys.serving import ModelLoader, ModelNotFound, load_model


class FakeTable:
    """model_versions in memory, patched in place of the SQL functions."""

    def __init__(self, *versions: ModelVersion) -> None:
        self.rows = {v.id: v for v in versions}
        self.fail_set_active = False

    def get(self, connection, version_id: int) -> ModelVersion | None:
        return self.rows.get(version_id)

    def get_active(self, connection) -> ModelVersion | None:
        return next((v for v in self.rows.values() if v.is_active), None)

    def set_active(self, connection, version_id: int) -> None:
        if self.fail_set_active:
            raise RuntimeError("database down")
        for v in self.rows.values():
            v.is_active = v.id == version_id


class FakeConnection:
    def commit(self) -> None: ...


@contextmanager
def connect():
    yield FakeConnection()


def version(version_id: int, active: bool = False) -> ModelVersion:
    return ModelVersion(version_id, f"model-{version_id}", datetime.now(UTC), active, None)


@pytest.fixture
def table(monkeypatch: pytest.MonkeyPatch) -> FakeTable:
    fake = FakeTable(version(1, active=True), version(2))
    for name in ("get", "get_active", "set_active"):
        monkeypatch.setattr(model_versions, name, getattr(fake, name))
    return fake


@dataclass
class FakeModel:
    model_version_id: int
    name: str

    def refresh_if_stale(self) -> None: ...


def fake_load(v: ModelVersion, models_dir: Path) -> FakeModel:
    return FakeModel(v.id, v.name)


def new_loader(registry: ModelRegistry) -> ModelLoader:
    return ModelLoader(connect, registry, Path("/models"), poll_seconds=60, load=fake_load)


def test_sync_loads_the_model_marked_active(table: FakeTable) -> None:
    registry = ModelRegistry()
    loader = new_loader(registry)

    loader.sync()
    assert registry.current.model_version_id == 1

    table.set_active(None, 2)  # activated elsewhere, e.g. by `recsys import-model`
    loader.sync()
    assert registry.current.model_version_id == 2


def test_activate_loads_then_marks_active(table: FakeTable) -> None:
    registry = ModelRegistry()

    new_loader(registry).activate(2)

    assert registry.current.model_version_id == 2
    assert table.get_active(None).id == 2


def test_activating_the_loaded_version_does_not_load_it_again(table: FakeTable) -> None:
    loads: list[int] = []

    def counting_load(v: ModelVersion, models_dir: Path) -> FakeModel:
        loads.append(v.id)
        return fake_load(v, models_dir)

    loader = ModelLoader(connect, ModelRegistry(), Path("/models"), poll_seconds=60, load=counting_load)
    loader.sync()
    loader.activate(1)

    assert loads == [1]


def test_activate_unknown_version_raises(table: FakeTable) -> None:
    with pytest.raises(ModelNotFound):
        new_loader(ModelRegistry()).activate(99)


def test_failed_activation_keeps_the_previous_model(table: FakeTable) -> None:
    registry = ModelRegistry()
    loader = new_loader(registry)
    loader.sync()
    table.fail_set_active = True

    with pytest.raises(RuntimeError):
        loader.activate(2)

    assert registry.current.model_version_id == 1
    assert table.get_active(None).id == 1


def test_load_model_requires_the_files(tmp_path: Path) -> None:
    with pytest.raises(ModelNotFound):
        load_model(version(5), tmp_path, catalog=None, history=None)

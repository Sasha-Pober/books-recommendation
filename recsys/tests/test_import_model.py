import json
import zipfile
from pathlib import Path

import pytest

from recsys import model_versions
from recsys.model.importer import Activation, _should_activate, _unpack, check_catalog
from tests.test_serving import version


def bundle_folder(root: Path) -> Path:
    root.mkdir(parents=True)
    (root / "bundle.json").write_text(json.dumps({"format": 2}))
    (root / "model.weights.h5").write_bytes(b"weights")
    return root


def test_unpack_accepts_a_folder(tmp_path: Path) -> None:
    folder = bundle_folder(tmp_path / "hybrid")

    assert _unpack(folder, tmp_path / "unused") == folder


@pytest.mark.parametrize("prefix", ["", "hybrid-20261118/"])
def test_unpack_accepts_a_zip_with_or_without_an_enclosing_folder(tmp_path: Path, prefix: str) -> None:
    archive = tmp_path / "model.zip"
    with zipfile.ZipFile(archive, "w") as z:
        z.writestr(f"{prefix}bundle.json", "{}")
        z.writestr(f"{prefix}model.weights.h5", "w")
    target = tmp_path / "out"
    target.mkdir()

    folder = _unpack(archive, target)

    assert (folder / "bundle.json").exists() and (folder / "model.weights.h5").exists()


def test_unpack_refuses_paths_outside_the_target(tmp_path: Path) -> None:
    archive = tmp_path / "evil.zip"
    with zipfile.ZipFile(archive, "w") as z:
        z.writestr("../escape.txt", "x")
    (tmp_path / "out").mkdir()

    with pytest.raises(ValueError, match="Unsafe path"):
        _unpack(archive, tmp_path / "out")


def metrics(ndcg: float) -> dict:
    return {"hybrid": {"test": {"ndcg@10": ndcg}}}


@pytest.mark.parametrize(
    ("active_ndcg", "new_ndcg", "expected"),
    [(None, 0.1, True), (0.2, 0.3, True), (0.3, 0.2, False), (0.3, 0.3, False)],
)
def test_auto_activation_only_for_a_better_model(
    monkeypatch: pytest.MonkeyPatch, active_ndcg: float | None, new_ndcg: float, expected: bool
) -> None:
    active = None if active_ndcg is None else version(1, active=True)
    if active is not None:
        active.metrics = metrics(active_ndcg)
    monkeypatch.setattr(model_versions, "get_active", lambda connection: active)

    assert _should_activate(None, Activation.AUTO, metrics(new_ndcg)) is expected
    assert _should_activate(None, Activation.YES, metrics(0.0)) is True
    assert _should_activate(None, Activation.NO, metrics(1.0)) is False


def test_refuses_a_model_whose_books_are_mostly_not_in_the_store() -> None:
    with pytest.raises(ValueError, match="import-goodreads"):
        check_catalog(missing=36131, total=36131, allow_missing_books=False)

    check_catalog(missing=36131, total=36131, allow_missing_books=True)
    check_catalog(missing=100, total=36131, allow_missing_books=False)  # a few deleted books are fine

from pathlib import Path

import numpy as np
import pandas as pd
import pytest
import torch

from recsys.dataset.pipeline import load_export
from recsys.model.bundle import load_bundle
from recsys.model.data import PAD, build_histories, build_training_data
from recsys.model.evaluate import evaluate, new_metrics
from recsys.model.metrics import RankingMetrics
from recsys.model.text import embed_texts
from recsys.model.train import TrainConfig, build_model, train
from tests.synthetic import fake_encoder, write_export


def test_ranking_metrics_by_hand() -> None:
    metrics = RankingMetrics(ks=[2], n_items=10)
    metrics.add(np.array([[3, 7], [1, 2]]), [np.array([7, 9]), np.array([5])])

    result = metrics.result()

    # User 1: hit at rank 2 of 2 relevant; user 2: no hits.
    assert result["precision@2"] == pytest.approx(0.25)
    assert result["recall@2"] == pytest.approx(0.25)
    assert result["hr@2"] == pytest.approx(0.5)
    assert result["mrr@2"] == pytest.approx(0.25)
    ideal = 1 + 1 / np.log2(3)
    assert result["ndcg@2"] == pytest.approx((1 / np.log2(3)) / ideal / 2)
    assert result["coverage@2"] == pytest.approx(0.4)


def test_beyond_accuracy_metrics() -> None:
    vectors = np.array([[1, 0], [1, 0], [0, 1]], dtype=np.float32)
    metrics = RankingMetrics(
        ks=[2], n_items=3, item_vectors=vectors, item_popularity=np.array([9, 0, 0]), n_train_users=9
    )

    metrics.add(np.array([[0, 1], [0, 2]]), [np.array([0]), np.array([0])])

    result = metrics.result()
    assert result["ild@2"] == pytest.approx(0.5)  # identical pair (0) and orthogonal pair (1)
    assert result["novelty@2"] > 0


def test_histories_keep_the_latest_items_newest_last_with_time_decay() -> None:
    t0 = pd.Timestamp("2020-12-31", tz="UTC")
    pairs = pd.DataFrame(
        {
            "user": [1, 1, 1],
            "item": [10, 11, 12],
            "r": [1.0, 0.5, 1.0],
            "t": pd.to_datetime(["2020-01-01", "2020-12-31", "2020-07-04"], utc=True),
        }
    )

    histories = build_histories(pairs, n_users=2, t0=t0, length=2, half_life_days=180)

    assert histories.items[1].tolist() == [12, 11]  # the oldest dropped, newest last
    assert histories.weights[1].tolist() == pytest.approx([0.5, 0.5], abs=0.01)  # 180 days old → half
    assert histories.items[0].tolist() == [PAD, PAD] and histories.items[2].tolist() == [PAD, PAD]


def test_text_embeddings_are_cached_per_text(tmp_path: Path) -> None:
    calls: list[list[str]] = []

    def encode(texts: list[str]) -> np.ndarray:
        calls.append(texts)
        return fake_encoder(texts)

    cache = tmp_path / "cache.npz"
    first = embed_texts(["sea a", "war b"], encode, cache)
    second = embed_texts(["war b", "sea c"], encode, cache)

    assert calls == [["sea a", "war b"], ["sea c"]] or calls[1] == ["sea c"]
    np.testing.assert_allclose(first[1], second[0])


@pytest.fixture(scope="module")
def trained(tmp_path_factory) -> tuple[Path, Path]:
    folder = tmp_path_factory.mktemp("synthetic")
    export = write_export(folder / "export")
    config = TrainConfig(
        max_epochs=8, patience=8, batch_size=128, eval_users=500, test_users=0, mf_epochs=5, n_min=3
    )
    bundle = train(export, folder / "bundles", config, device="cpu", name="test", encode=fake_encoder)
    return export, bundle


def test_training_learns_preferences_and_beats_popularity(trained) -> None:
    _, bundle_folder = trained
    metrics = load_bundle(bundle_folder).metrics

    hybrid = metrics["hybrid"]["test"]["ndcg@10"]
    assert hybrid > metrics["popularity"]["test"]["ndcg@10"]
    curve = metrics["training_curve"]
    assert curve[-1]["bpr"] < curve[0]["bpr"]
    assert "cold_val_ndcg@10" in curve[0]


def test_new_books_are_ranked_by_the_readers_taste(trained) -> None:
    """Books without train interactions: among them, the reader's preferred kind must come first.
    (A full-catalog hit rate can't show this here: each reader has ~15 equally good known books.)"""
    export, bundle_folder = trained
    model = load_bundle(bundle_folder).model
    data = build_training_data(load_export(export))
    kinds = np.array(["sea" in text for text in data.features.texts])
    new_books = np.flatnonzero(data.item_counts == 0)
    users = data.test.users
    likes_sea = np.array([int(data.user_ids[u - 1][1:]) % 2 == 0 for u in users])

    scores = model.score_catalog(
        torch.as_tensor(users),
        torch.as_tensor(data.test.histories.items[users]),
        torch.as_tensor(data.test.histories.weights[users]),
    ).numpy()

    correct = total = 0
    for row, likes in enumerate(likes_sea):
        liked = new_books[kinds[new_books] == likes]
        other = new_books[kinds[new_books] != likes]
        correct += int((scores[row, liked][:, None] > scores[row, other][None, :]).sum())
        total += len(liked) * len(other)
    assert correct / total > 0.9


def test_catalog_scores_equal_pairwise_scores(trained) -> None:
    export, bundle_folder = trained
    model = load_bundle(bundle_folder).model
    data = build_training_data(load_export(export), history_length=50)
    users = torch.as_tensor(data.test.users[:4])
    history = torch.as_tensor(data.test.histories.items[data.test.users[:4]])
    weights = torch.as_tensor(data.test.histories.weights[data.test.users[:4]])

    catalog = model.score_catalog(users, history, weights, chunk=7)
    with torch.no_grad():
        content = model.content()
        items = torch.tensor([[0, 5, 59]] * 4)
        pairwise = model.score(users, items, model.user_profile(content, history, weights), content).z

    torch.testing.assert_close(catalog[:, [0, 5, 59]], pairwise)


def test_new_books_use_the_content_projection(trained) -> None:
    _, bundle_folder = trained
    model = load_bundle(bundle_folder).model
    cold = int(torch.nonzero(model.item_counts < model.config.n_min)[0])
    users = torch.tensor([1])
    content = model.content()
    profile = torch.zeros(1, model.config.content_dim)

    before = model.score(users, torch.tensor([[cold]]), profile, content).z
    with torch.no_grad():
        model.item_gmf.weight[cold] += 100.0  # the id vector of a cold book is not used
    after = model.score(users, torch.tensor([[cold]]), profile, content).z

    torch.testing.assert_close(before, after)


def test_bundle_round_trip_gives_the_same_scores(trained) -> None:
    export, bundle_folder = trained
    bundle = load_bundle(bundle_folder)
    data = build_training_data(load_export(export))
    rebuilt = build_model(data, bundle.text_embeddings, TrainConfig(n_min=3))
    rebuilt.load_state_dict(bundle.model.state_dict())
    rebuilt.eval()

    ks, n = (10,), len(bundle.book_ids)
    args = (data.test, None, torch.device("cpu"))
    original = evaluate(
        bundle.model.score_catalog,
        args[0],
        new_metrics(ks, n, bundle.text_embeddings, data.item_counts, len(data.user_ids)),
        args[2],
    )
    again = evaluate(
        rebuilt.score_catalog,
        args[0],
        new_metrics(ks, n, bundle.text_embeddings, data.item_counts, len(data.user_ids)),
        args[2],
    )

    assert original == again
    assert bundle.book_ids == data.features.book_ids

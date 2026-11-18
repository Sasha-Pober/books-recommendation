from pathlib import Path

import numpy as np
import pandas as pd
import pytest
from keras import ops

from recsys.dataset.pipeline import load_export
from recsys.model.bundle import load_bundle
from recsys.model.data import PAD, build_histories, build_training_data
from recsys.model.evaluate import evaluate, new_metrics
from recsys.model.metrics import RankingMetrics
from recsys.model.text import embed_texts
from recsys.model.train import TrainConfig, build_model
from tests.synthetic import fake_encoder, user_number, write_export


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
    likes_sea = np.array([user_number(data.user_ids[u - 1]) % 2 == 0 for u in users])

    scores = model.score_catalog(users, data.test.histories.items[users], data.test.histories.weights[users])

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
    users = data.test.users[:4]
    history = data.test.histories.items[users]
    weights = data.test.histories.weights[users]

    catalog = model.score_catalog(users, history, weights, chunk=7)
    content = model.content()
    items = np.array([[0, 5, 59]] * 4)
    pairwise = model.score(users, items, model.user_profile(content, history, weights), content).z

    np.testing.assert_allclose(catalog[:, [0, 5, 59]], ops.convert_to_numpy(pairwise), rtol=1e-5, atol=1e-6)


def test_new_books_use_the_content_projection(trained) -> None:
    _, bundle_folder = trained
    model = load_bundle(bundle_folder).model
    cold = int(np.flatnonzero(ops.convert_to_numpy(model.item_counts) < model.config.n_min)[0])
    users, items = np.array([1]), np.array([[cold]])
    content = model.content()
    profile = np.zeros((1, model.config.content_dim), dtype=np.float32)

    before = ops.convert_to_numpy(model.score(users, items, profile, content).z)
    vectors = ops.convert_to_numpy(model.item_gmf.embeddings).copy()
    vectors[cold] += 100.0  # the id vector of a cold book is not used
    model.item_gmf.embeddings.assign(vectors)
    after = ops.convert_to_numpy(model.score(users, items, profile, content).z)

    np.testing.assert_allclose(before, after)


def test_bundle_round_trip_gives_the_same_scores(trained) -> None:
    export, bundle_folder = trained
    bundle = load_bundle(bundle_folder)
    data = build_training_data(load_export(export))
    rebuilt = build_model(data, bundle.text_embeddings, TrainConfig(n_min=3))
    rebuilt.set_weights(bundle.model.get_weights())

    ks, n = (10,), len(bundle.book_ids)
    original = evaluate(
        bundle.model.score_catalog,
        data.test,
        new_metrics(ks, n, bundle.text_embeddings, data.item_counts, len(data.user_ids)),
    )
    again = evaluate(
        rebuilt.score_catalog,
        data.test,
        new_metrics(ks, n, bundle.text_embeddings, data.item_counts, len(data.user_ids)),
    )

    assert original == again
    assert bundle.book_ids == data.features.book_ids


def test_books_without_interactions_are_left_out_of_training(tmp_path) -> None:
    """A Ukrainian book nobody has read yet: training leaves it (and its language, author, genre) out, so
    the model doesn't learn them as "disliked"; at inference it is a new book with the unknown language."""
    export = write_export(tmp_path / "export")
    books = pd.read_parquet(export / "books.parquet")
    extra = books.iloc[[0]].assign(
        id="00000000-0000-4000-8000-000000000999", title="Лісова пісня", language="uk"
    )
    pd.concat([books, extra]).to_parquet(export / "books.parquet")
    for name, column, value in (("book_authors", "author_id", "lesya"), ("book_genres", "genre_id", "drama")):
        links = pd.read_parquet(export / f"{name}.parquet")
        pd.concat([links, pd.DataFrame({"book_id": [extra["id"].item()], column: [value]})]).to_parquet(
            export / f"{name}.parquet"
        )

    data = build_training_data(load_export(export))

    assert extra["id"].item() not in data.features.book_ids
    assert "uk" not in data.features.language_vocab.keys
    assert "lesya" not in data.features.author_vocab.keys
    assert "drama" not in data.features.genre_vocab.keys
    assert data.features.language_vocab.index("uk") == 0  # → the zero "unknown" vector at inference

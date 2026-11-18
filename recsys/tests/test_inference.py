from pathlib import Path
from uuid import UUID

import numpy as np
import pandas as pd
import pytest

from recsys.api.schemas import HomeRequest, Reason, SimilarRequest
from recsys.inference import CatalogData, HybridRecommender, InferenceSettings, mmr
from recsys.model.bundle import load_bundle
from tests.synthetic import book_id, fake_encoder, user_id


def is_sea(book: str | UUID) -> bool:
    return int(str(book)[-12:]) % 2 == 0  # synthetic books alternate: sea, war, sea, ...


class MemoryCatalog:
    def __init__(self, export: Path) -> None:
        self.books = pd.read_parquet(export / "books.parquet")
        self.book_authors = pd.read_parquet(export / "book_authors.parquet")
        self.book_genres = pd.read_parquet(export / "book_genres.parquet")
        self.popular_new: list[str] = []
        self.loads = 0

    def load(self) -> CatalogData:
        self.loads += 1
        return CatalogData(
            self.books.copy(), self.book_authors.copy(), self.book_genres.copy(), self.popular_new
        )

    def add_book(self, number: int, kind: str, author: str) -> str:
        new_id = book_id(number)
        row = self.books.iloc[0].copy()
        row["id"], row["title"], row["annotation"] = (
            new_id,
            f"{kind} poems {number}",
            f"Poems about the {kind}.",
        )
        row["created_at"] = self.books["created_at"].max() + pd.Timedelta(days=1)
        self.books = pd.concat([self.books, row.to_frame().T], ignore_index=True)
        self.book_authors = pd.concat(
            [self.book_authors, pd.DataFrame({"book_id": [new_id], "author_id": [author]})], ignore_index=True
        )
        self.book_genres = pd.concat(
            [self.book_genres, pd.DataFrame({"book_id": [new_id], "genre_id": [kind]})], ignore_index=True
        )
        return new_id


class MemoryHistory:
    def __init__(self, export: Path) -> None:
        events = pd.read_parquet(export / "interactions.parquet")
        self.by_user = {u: frame for u, frame in events.groupby("user_id")}
        self.by_session: dict[str, pd.DataFrame] = {}

    def recent_events(self, user: UUID | None, session_id: str, limit: int) -> pd.DataFrame:
        empty = pd.DataFrame(columns=["book_id", "weight", "created_at"])
        frame = self.by_user.get(str(user), empty) if user else self.by_session.get(session_id, empty)
        return frame[["book_id", "weight", "created_at"]].sort_values("created_at").tail(limit)


@pytest.fixture
def setup(trained):
    export, bundle_folder = trained
    catalog, history = MemoryCatalog(export), MemoryHistory(export)
    encoded: list[str] = []

    def encoder():
        def encode(texts: list[str]) -> np.ndarray:
            encoded.extend(texts)
            return fake_encoder(texts)

        return encode

    def recommender(**settings) -> HybridRecommender:
        return HybridRecommender(
            7, "test", load_bundle(bundle_folder), catalog, history, encoder, InferenceSettings(**settings)
        )

    return recommender, catalog, history, encoded


def known_user(trained, sea: bool) -> str:
    bundle = load_bundle(trained[1])
    return next(u for u in bundle.user_ids if (int(u[-12:]) % 2 == 0) == sea)


def home(
    recommender: HybridRecommender, user: str | None = None, session: str = "s", count: int = 10, exclude=()
):
    request = HomeRequest(user_id=user, session_id=session, count=count, exclude_book_ids=list(exclude))
    return recommender.recommend_home(request)


@pytest.mark.parametrize("sea", [True, False])
def test_home_follows_the_readers_taste(setup, trained, sea: bool) -> None:
    recommender, *_ = setup
    response = home(recommender(), known_user(trained, sea))

    assert response.model_version_id == 7
    assert len(response.items) == 10
    assert sum(is_sea(i.book_id) == sea for i in response.items) >= 8
    assert all(0 < i.score < 1 and i.reason is not None for i in response.items)


def test_home_skips_excluded_and_out_of_stock_books(setup, trained) -> None:
    recommender, catalog, *_ = setup
    user = known_user(trained, sea=True)
    first = home(recommender(), user).items
    excluded, sold_out = first[0].book_id, first[1].book_id
    catalog.books.loc[catalog.books["id"] == str(sold_out), "stock"] = 0

    again = home(recommender(), user, exclude=[excluded]).items

    returned = {i.book_id for i in again}
    assert excluded not in returned and sold_out not in returned


def test_guest_gets_recommendations_from_the_session(setup) -> None:
    recommender, _, history, _ = setup
    war_books = [book_id(i) for i in (1, 3, 5)]
    history.by_session["guest"] = pd.DataFrame(
        {"book_id": war_books, "weight": 0.2, "created_at": pd.Timestamp("2016-06-01", tz="UTC")}
    )

    items = home(recommender(), session="guest").items

    assert items and sum(not is_sea(i.book_id) for i in items) >= 8


def test_unknown_visitor_without_history_gets_nothing_so_the_api_falls_back(setup) -> None:
    recommender, *_ = setup

    assert home(recommender(), session="nobody").items == []


def test_mmr_trades_relevance_for_diversity() -> None:
    vectors = np.array([[1, 0], [1, 0], [0, 1]], dtype=np.float32)  # 0 and 1 are duplicates
    relevance = np.array([0.9, 0.85, 0.6], dtype=np.float32)

    assert mmr(relevance, vectors, 2, mu=1.0) == [0, 1]  # relevance only
    assert mmr(relevance, vectors, 2, mu=0.5) == [0, 2]  # the duplicate is penalized


def test_new_book_after_training_is_recommended_as_new_from_a_favourite_author(setup, trained) -> None:
    recommender, catalog, history, encoded = setup
    user = known_user(trained, sea=True)
    favourite = catalog.book_authors.loc[
        catalog.book_authors["book_id"] == history.by_user[user]["book_id"].iloc[-1], "author_id"
    ].iloc[0]
    new_book = catalog.add_book(500, "sea", favourite)
    catalog.popular_new = [new_book]

    items = home(recommender(), user, count=60).items

    recommended = {str(i.book_id): i for i in items}
    assert new_book in recommended
    assert recommended[new_book].reason == Reason.NEW_FROM_FAVORITE_AUTHOR
    assert encoded == ["sea poems 500. Poems about the sea."]  # only the new book's text is embedded


def test_catalog_is_reloaded_when_stale_and_new_texts_are_embedded_once(setup) -> None:
    recommender, catalog, _, encoded = setup
    catalog.add_book(501, "war", "a1")
    model = recommender(catalog_refresh_seconds=0)

    model.refresh_if_stale()
    model.refresh_if_stale()

    assert catalog.loads == 3
    assert len(encoded) == 1


def test_similar_books_share_the_kind_and_explain_shared_authors(setup) -> None:
    recommender, catalog, *_ = setup
    source = book_id(0)  # sea

    items = recommender().find_similar(SimilarRequest(book_id=source, count=5)).items

    assert len(items) == 5 and str(source) not in {str(i.book_id) for i in items}
    assert sum(is_sea(i.book_id) for i in items) >= 4
    authors = catalog.book_authors.set_index("book_id")["author_id"]
    for item in items:
        shares_author = authors[str(item.book_id)] == authors[source]
        assert (item.reason == Reason.SAME_AUTHOR) == shares_author


def test_similar_for_an_unknown_book_is_empty(setup) -> None:
    recommender, *_ = setup

    assert recommender().find_similar(SimilarRequest(book_id=book_id(999), count=5)).items == []


def test_ids_are_uuids_in_the_contract(setup, trained) -> None:
    recommender, *_ = setup
    items = home(recommender(), user_id(0)).items

    assert all(isinstance(i.book_id, UUID) for i in items)


def test_profile_history_matches_training_semantics(setup) -> None:
    """r = max weight per book, timed when first reached; newest books kept; decay from the newest event."""
    recommender, *_ = setup
    model = recommender()
    state = model._state
    day = pd.Timestamp("2016-01-01", tz="UTC")
    events = pd.DataFrame(
        [
            (book_id(0), 0.2, day),  # viewed, then bought: r = 1.0 at day 10
            (book_id(0), 1.0, day + pd.Timedelta(days=10)),
            (book_id(0), 1.0, day + pd.Timedelta(days=20)),  # later equal weight: time stays day 10
            (book_id(1), 0.0, day + pd.Timedelta(days=5)),  # 1-3 star rating only: not part of the profile
            (book_id(2), 0.5, day + pd.Timedelta(days=190)),  # newest: no decay
            (book_id(999), 1.0, day),  # not in the catalog
        ],
        columns=["book_id", "weight", "created_at"],
    )

    items, weights = model._profile_history(events, state)

    assert [state.ids[i] for i in items] == [book_id(0), book_id(2)]  # oldest first, newest last
    np.testing.assert_allclose(weights, [1.0 * 0.5 ** (180 / 180), 0.5], rtol=1e-6)

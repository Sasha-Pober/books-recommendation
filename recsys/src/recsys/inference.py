"""Inference with an imported hybrid model (CLAUDE.md, "Інференс (≤ 100 мс)"):

1. profile of the user: u_c from their (or the guest session's) recent events, p_u if training knew the user;
   no history at all → no recommendations (the store API then shows its bestsellers / new releases);
2. candidates: nearest books to u_c (content index) + nearest to p_u (latent index) + popular new books,
   minus the books the API excludes (bought, rated) and books out of stock;
3. ranking by ŷ_ui = σ(α·s_cf + (1-α)·γ·s_c);
4. re-ranking with MMR: argmax [μ·ŷ_ui − (1−μ)·max_{j∈S} sim(i,j)];
5. explanation by the dominant component.

Books added to the store after training are part of the catalog: their content vector c_i comes from their
metadata and text, and NeuMF uses the content projection W_q c_i for them (cold start).
"""

import logging
import time
from collections.abc import Callable
from dataclasses import dataclass
from typing import Protocol
from uuid import UUID

import faiss
import numpy as np
import pandas as pd
from keras import ops

from recsys.api.schemas import (
    HomeRequest,
    Reason,
    RecommendationItem,
    RecommendationResponse,
    SimilarRequest,
)
from recsys.dataset.features import encode_books
from recsys.model.bundle import Bundle
from recsys.model.network import inference_mode
from recsys.model.text import Encoder

logger = logging.getLogger(__name__)


@dataclass
class CatalogData:
    """The store's catalog as the recommender needs it."""

    books: pd.DataFrame  # id, title, annotation, language, year, pages, publisher_id, stock, created_at
    book_authors: pd.DataFrame  # book_id, author_id
    book_genres: pd.DataFrame  # book_id, genre_id
    popular_new: list[str]  # ids of recently added books that are popular now, most popular first


class CatalogSource(Protocol):
    def load(self) -> CatalogData: ...


class HistorySource(Protocol):
    def recent_events(self, user_id: UUID | None, session_id: str, limit: int) -> pd.DataFrame:
        """book_id, weight, created_at: the newest events of the user, or of the guest session if user_id
        is None."""
        ...


@dataclass
class InferenceSettings:
    content_candidates: int = 200
    latent_candidates: int = 200
    new_candidates: int = 50
    mmr_mu: float = 0.8
    # Books added within this many days of the newest one count as new ("new from a favourite author").
    new_book_days: float = 180.0
    # Events read per request; the profile then keeps the training's history length.
    event_limit: int = 500
    catalog_refresh_seconds: float = 300.0


def mmr(relevance: np.ndarray, vectors: np.ndarray, count: int, mu: float) -> list[int]:
    """Greedy maximal marginal relevance: positions into relevance/vectors (unit rows), in pick order."""
    n = len(relevance)
    selected: list[int] = []
    max_similarity = np.zeros(n, dtype=np.float32)  # to the already selected; 0 for the first pick
    available = np.ones(n, dtype=bool)
    for _ in range(min(count, n)):
        value = mu * relevance - (1 - mu) * max_similarity
        value[~available] = -np.inf
        pick = int(np.argmax(value))
        selected.append(pick)
        available[pick] = False
        max_similarity = np.maximum(max_similarity, vectors @ vectors[pick])
    return selected


def _unit(vectors: np.ndarray) -> np.ndarray:
    vectors = np.ascontiguousarray(vectors, dtype=np.float32)
    norms = np.linalg.norm(vectors, axis=1, keepdims=True)
    return vectors / np.maximum(norms, 1e-12)


def _index(unit_vectors: np.ndarray) -> faiss.IndexFlatIP:
    """Exact inner-product search over unit vectors (= cosine): the catalog is small enough for brute
    force."""
    index = faiss.IndexFlatIP(unit_vectors.shape[1])
    index.add(unit_vectors)
    return index


@dataclass
class CatalogState:
    """Everything derived from one catalog snapshot. Replaced as a whole on refresh."""

    ids: list[str]  # item index → book id: the trained books first, then books added after training
    index_of: dict[str, int]
    content: object  # c_i [items, d], a backend tensor (scoring input)
    content_unit: np.ndarray  # c_i / ‖c_i‖, for the content index, MMR and "similar books"
    content_index: faiss.IndexFlatIP
    latent_unit: np.ndarray  # q_i (GMF part) / ‖q_i‖: id vectors, or W_q c_i for cold books
    latent_index: faiss.IndexFlatIP
    item_counts: np.ndarray  # n_i; 0 for books added after training
    in_stock: np.ndarray  # bool [items]; books no longer in the store are out of stock
    is_new: np.ndarray  # bool [items]
    authors: list[frozenset[str]]
    popular_new: np.ndarray  # item indices
    loaded_at: float


class HybridRecommender:
    """An imported model serving the store's live data (implements recsys.recommender.Recommender)."""

    def __init__(
        self,
        model_version_id: int,
        name: str,
        bundle: Bundle,
        catalog: CatalogSource,
        history: HistorySource,
        encoder: Callable[[], Encoder],
        settings: InferenceSettings | None = None,
    ) -> None:
        self.model_version_id = model_version_id
        self.name = name
        self._bundle = bundle
        self._model = bundle.model.eval()
        self._catalog = catalog
        self._history = history
        self._encoder_factory = encoder
        self._encoder: Encoder | None = None
        self._text_cache: dict[tuple[str, str], np.ndarray] = {}  # (book id, text) → embedding
        self._settings = settings or InferenceSettings()
        self._user_index = {user_id: i + 1 for i, user_id in enumerate(bundle.user_ids)}
        config = bundle.training.get("config", {})
        self._history_length = int(config.get("history_length", 50))
        self._half_life_days = float(config.get("decay_half_life_days", 180.0))
        self._state = self._build_state()

    # --- catalog -------------------------------------------------------------------------------------------

    def refresh_if_stale(self) -> None:
        if time.monotonic() - self._state.loaded_at >= self._settings.catalog_refresh_seconds:
            self._state = self._build_state()

    def _build_state(self) -> CatalogState:
        with inference_mode():
            return self._load_state()

    def _load_state(self) -> CatalogState:
        data = self._catalog.load()
        model, bundle = self._model, self._bundle
        books = data.books.assign(id=data.books["id"].astype(str)).set_index("id", drop=False)
        trained_ids = list(bundle.book_ids)
        trained = set(trained_ids)

        new_books = books[~books.index.isin(trained)]
        new_ids, new_content = self._content_of_new_books(new_books, data)
        ids = trained_ids + new_ids
        index_of = {book_id: i for i, book_id in enumerate(ids)}

        content = model.content()
        if new_content is not None:
            content = ops.concatenate([content, new_content], axis=0)
        item_counts = np.concatenate(
            [ops.convert_to_numpy(model.item_counts), np.zeros(len(new_ids), np.float32)]
        )

        all_items = np.arange(len(ids))
        q_gmf, _ = model.item_vectors(all_items, content, item_counts < model.config.n_min)
        content_unit, latent_unit = _unit(ops.convert_to_numpy(content)), _unit(ops.convert_to_numpy(q_gmf))

        stock = books["stock"].reindex(ids).fillna(0).to_numpy()
        created = pd.to_datetime(books["created_at"].reindex(ids), utc=True)
        newest = created.max()
        cutoff = newest - pd.Timedelta(days=self._settings.new_book_days) if pd.notna(newest) else None
        is_new = np.array(
            [
                i >= len(trained_ids) or (cutoff is not None and pd.notna(t) and t >= cutoff)
                for i, t in enumerate(created)
            ]
        )

        authors_by_book = data.book_authors.assign(book_id=data.book_authors["book_id"].astype(str)).groupby(
            "book_id"
        )["author_id"]
        author_sets = {book_id: frozenset(map(str, group)) for book_id, group in authors_by_book}
        popular_new = np.array(
            [index_of[b] for b in map(str, data.popular_new) if b in index_of], dtype=np.int64
        )

        logger.info(
            "Catalog loaded for model '%s': %d books (%d added after training), %d in stock",
            self.name, len(ids), len(new_ids), int((stock > 0).sum()),
        )  # fmt: skip
        return CatalogState(
            ids=ids,
            index_of=index_of,
            content=content,
            content_unit=content_unit,
            content_index=_index(content_unit),
            latent_unit=latent_unit,
            latent_index=_index(latent_unit),
            item_counts=item_counts,
            in_stock=stock > 0,
            is_new=is_new,
            authors=[author_sets.get(book_id, frozenset()) for book_id in ids],
            popular_new=popular_new,
            loaded_at=time.monotonic(),
        )

    def _content_of_new_books(
        self, books: pd.DataFrame, data: CatalogData
    ) -> tuple[list[str], object | None]:
        """c_i of books the model hasn't seen, encoded with the trained vocabularies. Books whose text
        can't be embedded (e.g. the text model is unavailable) are left out until the next refresh."""
        if books.empty:
            return [], None
        bundle, model = self._bundle, self._model
        new_ids = set(books.index)
        features = encode_books(
            books.reset_index(drop=True),
            data.book_authors[data.book_authors["book_id"].astype(str).isin(new_ids)],
            data.book_genres[data.book_genres["book_id"].astype(str).isin(new_ids)],
            max_authors=model.config.author_slots,
            max_genres=model.config.genre_slots,
            vocabularies=bundle.vocabularies,
            numeric_stats=bundle.numeric_stats,
        )
        try:
            text = self._embed(features.book_ids, features.texts)
        except Exception as error:
            logger.warning("Can't embed %d new books, leaving them out: %s", len(features.book_ids), error)
            return [], None
        content = model.content_of(
            text,
            features.authors.astype(np.int32),
            features.genres.astype(np.int32),
            features.publisher.astype(np.int32),
            features.language.astype(np.int32),
            features.numeric,
        )
        return features.book_ids, content

    def _embed(self, book_ids: list[str], texts: list[str]) -> np.ndarray:
        keys = list(zip(book_ids, texts, strict=True))
        missing = [k for k in keys if k not in self._text_cache]
        if missing:
            if self._encoder is None:
                self._encoder = self._encoder_factory()
            vectors = np.asarray(self._encoder([text for _, text in missing]), dtype=np.float32)
            self._text_cache.update(zip(missing, vectors, strict=True))
        return np.stack([self._text_cache[k] for k in keys])

    # --- "Recommended for you" -----------------------------------------------------------------------------

    def recommend_home(self, request: HomeRequest) -> RecommendationResponse:
        with inference_mode():
            return self._recommend_home(request)

    def _recommend_home(self, request: HomeRequest) -> RecommendationResponse:
        state = self._state
        events = self._history.recent_events(request.user_id, request.session_id, self._settings.event_limit)
        history, weights = self._profile_history(events, state)
        user = self._user_index.get(str(request.user_id), 0) if request.user_id else 0
        if not len(history) and user == 0:
            # Nothing is known about this visitor yet: the API's non-personalized collections are better.
            return RecommendationResponse(model_version_id=self.model_version_id, items=[])

        model = self._model
        profile = model.user_profile(state.content, history[None].astype(np.int32), weights[None])
        excluded = {state.index_of[str(b)] for b in request.exclude_book_ids if str(b) in state.index_of}
        candidates = self._home_candidates(state, ops.convert_to_numpy(profile), user, excluded)
        if not len(candidates):
            return RecommendationResponse(model_version_id=self.model_version_id, items=[])

        scores = model.score(
            np.array([user], dtype=np.int32),
            candidates[None].astype(np.int32),
            profile,
            state.content,
            item_counts=state.item_counts,
        )
        relevance = ops.convert_to_numpy(ops.sigmoid(scores.z[0]))
        order = mmr(relevance, state.content_unit[candidates], request.count, self._settings.mmr_mu)

        favourite_authors = (
            frozenset().union(*(state.authors[i] for i in history)) if len(history) else frozenset()
        )
        collaborative = ops.convert_to_numpy(scores.alpha * scores.s_cf)[0]
        content_part = ops.convert_to_numpy((1 - scores.alpha) * model.gamma * scores.s_c)[0]
        items = []
        for position in order:
            item = int(candidates[position])
            if state.is_new[item] and state.authors[item] & favourite_authors:
                reason = Reason.NEW_FROM_FAVORITE_AUTHOR
            elif collaborative[position] > content_part[position]:
                reason = Reason.LIKED_BY_SIMILAR_READERS
            else:
                reason = Reason.SIMILAR_TO_VIEWED
            items.append(
                RecommendationItem(
                    book_id=UUID(state.ids[item]), score=float(relevance[position]), reason=reason
                )
            )
        return RecommendationResponse(model_version_id=self.model_version_id, items=items)

    def _profile_history(self, events: pd.DataFrame, state: CatalogState) -> tuple[np.ndarray, np.ndarray]:
        """Item indices and weights of the user's recent books, as in training: r = max weight per book (time:
        when it was first reached), newest `history_length` books, decayed by age. Ages are measured from the
        newest event rather than from now: the profile is a weighted mean, so a common factor cancels out,
        and old data (e.g. an imported dataset) doesn't underflow to zero."""
        empty = np.empty(0, dtype=np.int64), np.empty(0, dtype=np.float32)
        if events.empty:
            return empty
        # Plain lookups and numpy: per-request pandas operations against the catalog (e.g. isin with the
        # 36k-key index) cost over 100 ms, the whole latency budget.
        items = np.array([state.index_of.get(str(b), -1) for b in events["book_id"]], dtype=np.int64)
        weights = events["weight"].to_numpy(dtype=np.float64)
        times = (
            pd.to_datetime(events["created_at"], utc=True).to_numpy(dtype="datetime64[ns]").astype(np.int64)
        )
        known = items >= 0
        items, weights, times = items[known], weights[known], times[known]

        # Per book: the highest weight, at the earliest time it was reached (lexsort: last key is primary).
        order = np.lexsort((times, -weights, items))
        first = np.ones(len(order), dtype=bool)
        first[1:] = items[order][1:] != items[order][:-1]
        pick = order[first]
        pick = pick[weights[pick] > 0]
        pick = pick[np.argsort(times[pick], kind="stable")][-self._history_length :]
        if not len(pick):
            return empty

        age_days = (times[pick].max() - times[pick]) / 86_400e9
        decayed = weights[pick] * np.power(0.5, age_days / self._half_life_days)
        return items[pick], decayed.astype(np.float32)

    def _home_candidates(
        self, state: CatalogState, profile: np.ndarray, user: int, excluded: set[int]
    ) -> np.ndarray:
        settings = self._settings
        found: list[np.ndarray] = []
        if float(np.linalg.norm(profile)) > 0:
            _, nearest = state.content_index.search(
                _unit(profile), self._wanted(settings.content_candidates, state)
            )
            found.append(nearest[0])
        if user != 0:
            p_u = ops.convert_to_numpy(self._model.user_gmf.embeddings[user : user + 1])
            _, nearest = state.latent_index.search(
                _unit(p_u), self._wanted(settings.latent_candidates, state)
            )
            found.append(nearest[0])
        found.append(state.popular_new[: settings.new_candidates])

        candidates = pd.unique(np.concatenate(found).astype(np.int64))
        keep = (candidates >= 0) & state.in_stock[np.maximum(candidates, 0)]
        keep &= ~np.isin(candidates, list(excluded))
        return candidates[keep]

    @staticmethod
    def _wanted(count: int, state: CatalogState) -> int:
        """Ask the index for extra results, as some are filtered out (out of stock, excluded)."""
        return min(len(state.ids), count * 2)

    # --- "Similar books" -----------------------------------------------------------------------------------

    def find_similar(self, request: SimilarRequest) -> RecommendationResponse:
        """Content similarity to the book; plus collaborative similarity of the learned book vectors when both
        books have enough interactions (n_i ≥ n_min) for those vectors to mean something."""
        state = self._state
        source = state.index_of.get(str(request.book_id))
        if source is None:  # added since the last catalog refresh
            return RecommendationResponse(model_version_id=self.model_version_id, items=[])

        n_min = self._model.config.n_min
        wanted = self._wanted(request.count + 1, state)
        _, by_content = state.content_index.search(state.content_unit[source : source + 1], wanted)
        found = [by_content[0]]
        warm_source = float(state.item_counts[source]) >= n_min
        if warm_source:
            _, by_latent = state.latent_index.search(state.latent_unit[source : source + 1], wanted)
            found.append(by_latent[0])

        excluded = {state.index_of[str(b)] for b in request.exclude_book_ids if str(b) in state.index_of}
        excluded.add(source)
        candidates = pd.unique(np.concatenate(found).astype(np.int64))
        candidates = candidates[
            (candidates >= 0)
            & state.in_stock[np.maximum(candidates, 0)]
            & ~np.isin(candidates, list(excluded))
        ]
        if not len(candidates):
            return RecommendationResponse(model_version_id=self.model_version_id, items=[])

        content_similarity = state.content_unit[candidates] @ state.content_unit[source]
        latent_similarity = state.latent_unit[candidates] @ state.latent_unit[source]
        both_warm = warm_source & (state.item_counts[candidates] >= n_min)
        score = np.where(both_warm, 0.5 * content_similarity + 0.5 * latent_similarity, content_similarity)

        items = []
        for position in np.argsort(-score, kind="stable")[: request.count]:
            item = int(candidates[position])
            if state.authors[item] & state.authors[source]:
                reason = Reason.SAME_AUTHOR
            elif both_warm[position] and latent_similarity[position] > content_similarity[position]:
                reason = Reason.LIKED_BY_SIMILAR_READERS
            else:
                reason = Reason.SIMILAR_CONTENT
            items.append(
                RecommendationItem(book_id=UUID(state.ids[item]), score=float(score[position]), reason=reason)
            )
        return RecommendationResponse(model_version_id=self.model_version_id, items=items)

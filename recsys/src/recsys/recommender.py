import threading
from typing import Protocol

from recsys.api.schemas import HomeRequest, RecommendationResponse, SimilarRequest


class Recommender(Protocol):
    """A loaded model that serves recommendations (implemented by the trained hybrid model)."""

    model_version_id: int | None
    name: str

    def recommend_home(self, request: HomeRequest) -> RecommendationResponse: ...

    def find_similar(self, request: SimilarRequest) -> RecommendationResponse: ...

    def refresh_if_stale(self) -> None:
        """Reloads live data (e.g. the catalog) when it is old enough."""
        ...


class ModelRegistry:
    """Holds the model currently in use; swapped atomically when a new model is activated."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._current: Recommender | None = None

    @property
    def current(self) -> Recommender | None:
        return self._current

    def activate(self, recommender: Recommender) -> None:
        with self._lock:
            self._current = recommender

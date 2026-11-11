from uuid import UUID, uuid4

import pytest
from fastapi.testclient import TestClient

from recsys.api.schemas import (
    HomeRequest,
    Reason,
    RecommendationItem,
    RecommendationResponse,
    SimilarRequest,
)

BOOK = UUID("01990000-0000-7000-8000-000000000001")


class FakeRecommender:
    model_version_id = 3

    def __init__(self) -> None:
        self.home_requests: list[HomeRequest] = []

    def recommend_home(self, request: HomeRequest) -> RecommendationResponse:
        self.home_requests.append(request)
        return RecommendationResponse(
            model_version_id=self.model_version_id,
            items=[RecommendationItem(book_id=BOOK, score=0.91, reason=Reason.SIMILAR_TO_VIEWED)],
        )

    def find_similar(self, request: SimilarRequest) -> RecommendationResponse:
        return RecommendationResponse(model_version_id=self.model_version_id, items=[])


def home_body(**overrides) -> dict:
    return {"user_id": None, "session_id": "session-1", "count": 20, "exclude_book_ids": []} | overrides


@pytest.mark.parametrize("headers", [{}, {"X-Service-Key": "wrong-key"}])
def test_requests_without_the_service_key_are_rejected(client: TestClient, headers: dict) -> None:
    client.headers.clear()

    response = client.post("/v1/recommendations/home", json=home_body(), headers=headers)

    assert response.status_code == 401


def test_without_a_trained_model_returns_503_so_the_api_falls_back(client: TestClient) -> None:
    response = client.post("/v1/recommendations/home", json=home_body())

    assert response.status_code == 503
    assert response.headers["Retry-After"] == "60"


def test_home_follows_the_api_contract(client: TestClient) -> None:
    model = FakeRecommender()
    client.app.state.registry.activate(model)
    user_id, excluded = uuid4(), uuid4()

    response = client.post(
        "/v1/recommendations/home",
        json=home_body(user_id=str(user_id), exclude_book_ids=[str(excluded)]),
    )

    assert response.status_code == 200
    assert response.json() == {
        "model_version_id": 3,
        "items": [{"book_id": str(BOOK), "score": 0.91, "reason": "similar_to_viewed"}],
    }
    sent = model.home_requests[0]
    assert (sent.user_id, sent.session_id, sent.exclude_book_ids) == (user_id, "session-1", [excluded])


@pytest.mark.parametrize("body", [home_body(count=0), home_body(session_id=""), {"count": 5}])
def test_invalid_requests_are_rejected(client: TestClient, body: dict) -> None:
    client.app.state.registry.activate(FakeRecommender())

    assert client.post("/v1/recommendations/home", json=body).status_code == 422


def test_similar_requires_a_book_id(client: TestClient) -> None:
    client.app.state.registry.activate(FakeRecommender())

    ok = client.post("/v1/recommendations/similar", json={"book_id": str(BOOK), "count": 5})
    missing = client.post("/v1/recommendations/similar", json={"count": 5})

    assert ok.status_code == 200
    assert missing.status_code == 422


def test_training_is_not_available_yet(client: TestClient) -> None:
    assert client.post("/v1/training/jobs").status_code == 503


def test_health_is_public_and_reports_missing_database_and_model(client: TestClient) -> None:
    client.headers.clear()

    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "degraded", "database": False, "model_version_id": None}

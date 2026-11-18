from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException, Request, status

from recsys.api.schemas import (
    HealthResponse,
    HomeRequest,
    ModelActivationResponse,
    RecommendationResponse,
    SimilarRequest,
)
from recsys.db import Database
from recsys.recommender import ModelRegistry, Recommender
from recsys.security import require_service_key
from recsys.serving import ModelLoader, ModelNotFound

router = APIRouter()
v1 = APIRouter(prefix="/v1", dependencies=[Depends(require_service_key)])

# The API falls back to its own collections on any non-2xx response.
NO_MODEL_RETRY_AFTER = "60"


def get_registry(request: Request) -> ModelRegistry:
    return request.app.state.registry


def get_database(request: Request) -> Database:
    return request.app.state.database


def get_loader(request: Request) -> ModelLoader:
    return request.app.state.loader


def require_model(registry: Annotated[ModelRegistry, Depends(get_registry)]) -> Recommender:
    model = registry.current
    if model is None:
        raise HTTPException(
            status.HTTP_503_SERVICE_UNAVAILABLE,
            "No trained model is available yet.",
            headers={"Retry-After": NO_MODEL_RETRY_AFTER},
        )
    return model


@v1.post("/recommendations/home")
def recommend_home(
    body: HomeRequest, model: Annotated[Recommender, Depends(require_model)]
) -> RecommendationResponse:
    """'Recommended for you' for a user, or for a guest from the session's views."""
    return model.recommend_home(body)


@v1.post("/recommendations/similar")
def find_similar(
    body: SimilarRequest, model: Annotated[Recommender, Depends(require_model)]
) -> RecommendationResponse:
    """'Similar books' for a book page."""
    return model.find_similar(body)


@v1.post("/models/{model_version_id}/activate")
def activate_model(
    model_version_id: int, loader: Annotated[ModelLoader, Depends(get_loader)]
) -> ModelActivationResponse:
    """Loads an imported model version and marks it active (404 if the version or its files are unknown)."""
    try:
        loader.activate(model_version_id)
    except ModelNotFound as error:
        raise HTTPException(status.HTTP_404_NOT_FOUND, str(error)) from error
    return ModelActivationResponse(model_version_id=model_version_id)


@router.get("/health")
def health(
    database: Annotated[Database, Depends(get_database)],
    registry: Annotated[ModelRegistry, Depends(get_registry)],
) -> HealthResponse:
    """Unauthenticated, for the container health check. 200 even when degraded: the service is up."""
    db_ok = database.is_available()
    model = registry.current
    return HealthResponse(
        status="ok" if db_ok and model is not None else "degraded",
        database=db_ok,
        model_version_id=model.model_version_id if model is not None else None,
    )


router.include_router(v1)

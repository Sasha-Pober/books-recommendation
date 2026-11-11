"""HTTP contract with the ASP.NET Core API (RecommendationServiceClient). JSON is snake_case."""

from enum import StrEnum
from uuid import UUID

from pydantic import BaseModel, Field


class Reason(StrEnum):
    """Why a book is recommended; the API turns it into a short Ukrainian explanation."""

    SIMILAR_TO_VIEWED = "similar_to_viewed"
    LIKED_BY_SIMILAR_READERS = "liked_by_similar_readers"
    NEW_FROM_FAVORITE_AUTHOR = "new_from_favorite_author"
    BESTSELLER = "bestseller"
    TRENDING = "trending"
    NEW_RELEASE = "new_release"
    SAME_AUTHOR = "same_author"
    SIMILAR_CONTENT = "similar_content"


class HomeRequest(BaseModel):
    user_id: UUID | None = Field(default=None, description="Null for guests.")
    session_id: str = Field(min_length=1, max_length=100, description="Browser session (a guest's profile).")
    count: int = Field(ge=1, le=200)
    exclude_book_ids: list[UUID] = Field(default_factory=list, max_length=5000)


class SimilarRequest(BaseModel):
    book_id: UUID
    count: int = Field(ge=1, le=200)
    exclude_book_ids: list[UUID] = Field(default_factory=list, max_length=5000)


class RecommendationItem(BaseModel):
    book_id: UUID
    score: float
    reason: Reason | None = None


class RecommendationResponse(BaseModel):
    model_version_id: int | None = Field(description="Row in model_versions; null if not registered.")
    items: list[RecommendationItem]


class TrainingJobResponse(BaseModel):
    job_id: str
    status: str


class HealthResponse(BaseModel):
    status: str = Field(description='"ok" with database and model available, otherwise "degraded".')
    database: bool
    model_version_id: int | None

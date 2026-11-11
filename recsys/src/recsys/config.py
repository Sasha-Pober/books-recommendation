from pathlib import Path

from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Configuration from environment variables prefixed with RECSYS_ (or a .env file)."""

    model_config = SettingsConfigDict(env_prefix="RECSYS_", env_file=".env", extra="ignore")

    # The store database (shared with the ASP.NET Core API), e.g. postgresql://user:pass@postgres:5432/bookstore
    database_url: str

    # Shared secret the API sends as X-Service-Key.
    service_key: SecretStr = Field(min_length=16)

    # Trained models, indexes and cached text embeddings.
    artifacts_dir: Path = Path("artifacts")

    # Seconds to wait for a database connection before reporting it unavailable.
    database_timeout: float = 5.0

    host: str = "0.0.0.0"
    port: int = 8000

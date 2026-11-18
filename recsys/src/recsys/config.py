from pathlib import Path

from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Configuration from environment variables prefixed with RECSYS_ (or a .env file)."""

    model_config = SettingsConfigDict(env_prefix="RECSYS_", env_file=".env", extra="ignore")

    # The store database (shared with the ASP.NET Core API), e.g. postgresql://user:pass@postgres:5432/bookstore
    database_url: str

    # Shared secret the API sends as X-Service-Key. Required to serve; not needed by the data commands.
    service_key: SecretStr | None = Field(default=None, min_length=16)

    # Trained models, indexes and cached text embeddings. Models: <artifacts_dir>/models/<name>/.
    artifacts_dir: Path = Path("artifacts")

    # How often the service checks model_versions for a newly activated model.
    model_poll_seconds: float = 30.0

    # How often the active model reloads the catalog (new books, stock).
    catalog_refresh_seconds: float = 300.0

    # Seconds to wait for a database connection before reporting it unavailable.
    database_timeout: float = 5.0

    @property
    def models_dir(self) -> Path:
        return self.artifacts_dir / "models"

    host: str = "0.0.0.0"
    port: int = 8000

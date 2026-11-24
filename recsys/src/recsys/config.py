from pathlib import Path

from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict

# recsys/.env when running from a source checkout (src/recsys/config.py → recsys/), whatever the current
# directory; an installed package (Docker, Kaggle) has no such file and uses environment variables.
_PROJECT_ROOT = Path(__file__).resolve().parents[2]
_PROJECT_ENV = _PROJECT_ROOT / ".env"
# recsys/artifacts in a checkout (also /app/artifacts in Docker, where the project lives in /app), so
# `recsys import-model` puts models where the service looks, whatever the current directory.
# An installed package (Kaggle) uses ./artifacts.
_ARTIFACTS = _PROJECT_ROOT / "artifacts" if (_PROJECT_ROOT / "pyproject.toml").exists() else Path("artifacts")


class Settings(BaseSettings):
    """Configuration from environment variables prefixed with RECSYS_, or a .env file: the project's
    recsys/.env, then ./.env (later files win; environment variables win over both)."""

    model_config = SettingsConfigDict(env_prefix="RECSYS_", env_file=(_PROJECT_ENV, ".env"), extra="ignore")

    # The store database (shared with the ASP.NET Core API), e.g. postgresql://user:pass@postgres:5432/bookstore
    database_url: str

    # Shared secret the API sends as X-Service-Key. Required to serve; not needed by the data commands.
    service_key: SecretStr | None = Field(default=None, min_length=16)

    # Trained models, indexes and cached text embeddings. Models: <artifacts_dir>/models/<name>/.
    artifacts_dir: Path = _ARTIFACTS

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

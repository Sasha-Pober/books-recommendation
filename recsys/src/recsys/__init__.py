"""Recommendation service of the book store: hybrid (content + collaborative) neural model."""


def main() -> None:
    """Runs the API server (`uv run recsys`)."""
    import uvicorn

    from recsys.config import Settings

    settings = Settings()
    uvicorn.run("recsys.main:create_app", factory=True, host=settings.host, port=settings.port)

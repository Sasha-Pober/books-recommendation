import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI

from recsys.api.routes import router
from recsys.config import Settings
from recsys.db import Database
from recsys.recommender import ModelRegistry


def create_app(settings: Settings | None = None) -> FastAPI:
    settings = settings or Settings()
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
        app.state.database.open()
        try:
            yield
        finally:
            app.state.database.close()

    app = FastAPI(
        title="Book store recommendation service",
        version="0.1.0",
        lifespan=lifespan,
    )
    app.state.settings = settings
    app.state.database = Database(settings.database_url, settings.database_timeout)
    app.state.registry = ModelRegistry()
    app.include_router(router)
    return app

import secrets
from typing import Annotated

from fastapi import Depends, Header, HTTPException, Request, status

from recsys.config import Settings

SERVICE_KEY_HEADER = "X-Service-Key"


def get_settings(request: Request) -> Settings:
    return request.app.state.settings


def require_service_key(
    settings: Annotated[Settings, Depends(get_settings)],
    x_service_key: Annotated[str | None, Header(alias=SERVICE_KEY_HEADER)] = None,
) -> None:
    """Only the store API may call the service (it is also not exposed outside the internal network)."""
    expected = settings.service_key.get_secret_value()
    if x_service_key is None or not secrets.compare_digest(x_service_key.encode(), expected.encode()):
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Invalid or missing service key.")

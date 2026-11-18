FROM python:3.12-slim

COPY --from=ghcr.io/astral-sh/uv:0.13.7 /uv /bin/uv

ENV UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy \
    UV_PROJECT_ENVIRONMENT=/app/.venv \
    PATH="/app/.venv/bin:$PATH"

WORKDIR /app

# Dependencies first, in their own layer: cached until pyproject.toml / uv.lock change.
COPY pyproject.toml uv.lock ./
# The CPU build of PyTorch: the service loads models and runs inference on the CPU.
RUN uv sync --frozen --no-dev --extra cpu --no-install-project

COPY src/ src/
RUN uv sync --frozen --no-dev --extra cpu

RUN useradd --system --uid 10001 recsys && mkdir -p /app/hf-cache && chown recsys /app/hf-cache
USER recsys

EXPOSE 8000
HEALTHCHECK --interval=10s --timeout=3s --retries=5 \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8000/health')"
CMD ["uvicorn", "recsys.main:create_app", "--factory", "--host", "0.0.0.0", "--port", "8000"]

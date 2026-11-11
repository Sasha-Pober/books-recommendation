import logging

from psycopg_pool import ConnectionPool

logger = logging.getLogger(__name__)


class Database:
    """Connection pool to the store database. Opened without waiting, so the service starts even if
    PostgreSQL is not up yet; health reports it."""

    def __init__(self, url: str, timeout: float) -> None:
        self._timeout = timeout
        self.pool = ConnectionPool(url, min_size=1, max_size=5, open=False, timeout=timeout)

    def open(self) -> None:
        self.pool.open(wait=False)

    def close(self) -> None:
        self.pool.close()

    def is_available(self) -> bool:
        try:
            with self.pool.connection(timeout=self._timeout) as connection:
                connection.execute("SELECT 1")
            return True
        except Exception as error:  # any failure means "not available" for health
            logger.warning("Database is not available: %s", error)
            return False

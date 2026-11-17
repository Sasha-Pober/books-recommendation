import uuid

# Fixed namespace: the same source record always gets the same id, so imports can be repeated safely.
_NAMESPACE = uuid.UUID("6f1d2c3e-6a0b-4c8e-9d5f-2b7a1e4c9d10")


def stable_id(kind: str, key: str) -> uuid.UUID:
    """Deterministic UUID for an imported record, e.g. stable_id("goodreads:book", "16037549")."""
    return uuid.uuid5(_NAMESPACE, f"{kind}:{key}")

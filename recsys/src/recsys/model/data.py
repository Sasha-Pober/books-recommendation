"""Index mappings, user histories and evaluation sets built from an exported dataset."""

from dataclasses import dataclass

import numpy as np
import pandas as pd

from recsys.dataset.features import BookFeatures, encode_books
from recsys.dataset.pipeline import ExportedData, Split, build_pairs, time_split

PAD = -1  # history padding (no item)


@dataclass
class Histories:
    """The last `length` interactions of every user, newest last. Row 0 is the unknown user (empty)."""

    items: np.ndarray  # int64 [users + 1, length], PAD where empty
    weights: np.ndarray  # float32 [users + 1, length]: r_ui · time decay, 0 where empty


@dataclass
class EvalSet:
    """Users to rank the whole catalog for, the books that count as hits, and the books to skip."""

    users: np.ndarray  # int64 user indices
    relevant: list[np.ndarray]  # per user: item indices
    exclude: list[np.ndarray]  # per user: item indices already known (not recommended again)
    histories: Histories  # profiles as of the evaluation time


@dataclass
class TrainingData:
    features: BookFeatures
    user_ids: list[str]  # user index i+1 → id; index 0 is "unknown user"
    train_users: np.ndarray  # int64 [positives]
    train_items: np.ndarray  # int64 [positives]
    item_counts: np.ndarray  # float32 [items]: n_i, positives in train
    user_counts: np.ndarray  # float32 [users + 1]: n_u, positives in train
    train_histories: Histories
    val: EvalSet
    cold_val: EvalSet
    test: EvalSet
    cold_test: EvalSet
    split: Split


def decay_weights(r: np.ndarray, t: pd.Series, t0: pd.Timestamp, half_life_days: float) -> np.ndarray:
    """r · exp(-λ(t0 - t)) with λ = ln 2 / half-life: an interaction half_life_days old counts half."""
    age_days = np.maximum((t0 - t).dt.total_seconds().to_numpy() / 86400.0, 0.0)
    return (r * np.power(0.5, age_days / half_life_days)).astype(np.float32)


def build_histories(
    pairs: pd.DataFrame, n_users: int, t0: pd.Timestamp, length: int, half_life_days: float
) -> Histories:
    """pairs: user (index), item (index), r, t. Keeps each user's `length` most recent pairs."""
    items = np.full((n_users + 1, length), PAD, dtype=np.int64)
    weights = np.zeros((n_users + 1, length), dtype=np.float32)
    if pairs.empty:
        return Histories(items, weights)

    recent = pairs.sort_values("t").groupby("user", sort=False).tail(length)
    recent = recent.assign(w=decay_weights(recent["r"].to_numpy(), recent["t"], t0, half_life_days))
    recent = recent.sort_values(["user", "t"])
    position = recent.groupby("user").cumcount().to_numpy()
    offset = length - recent.groupby("user")["item"].transform("size").to_numpy()  # newest last
    users = recent["user"].to_numpy()
    items[users, offset + position] = recent["item"].to_numpy()
    weights[users, offset + position] = recent["w"].to_numpy()
    return Histories(items, weights)


def _grouped(frame: pd.DataFrame, users: np.ndarray) -> list[np.ndarray]:
    groups = frame.groupby("user")["item"].agg(lambda s: s.to_numpy())
    return [groups.get(u, np.empty(0, dtype=np.int64)) for u in users]


def build_training_data(
    export: ExportedData,
    val_fraction: float = 0.1,
    test_fraction: float = 0.1,
    history_length: int = 50,
    half_life_days: float = 180.0,
) -> TrainingData:
    features = encode_books(export.books, export.book_authors, export.book_genres)
    book_index = {book_id: i for i, book_id in enumerate(features.book_ids)}

    pairs = build_pairs(export.interactions)
    pairs = pairs[pairs["book_id"].astype(str).isin(book_index)]
    split = time_split(pairs, val_fraction, test_fraction)

    user_ids = sorted(split.train["user_id"].astype(str).unique())
    user_index = {user_id: i + 1 for i, user_id in enumerate(user_ids)}

    def indexed(frame: pd.DataFrame) -> pd.DataFrame:
        return pd.DataFrame(
            {
                "user": frame["user_id"].astype(str).map(user_index).fillna(0).astype(np.int64).to_numpy(),
                "item": frame["book_id"].astype(str).map(book_index).astype(np.int64).to_numpy(),
                "r": frame["r"].to_numpy(dtype=np.float32),
                "t": frame["t"].to_numpy(),
            }
        ).assign(t=lambda f: pd.to_datetime(f["t"], utc=True))

    train, val, test = (indexed(f) for f in (split.train, split.val, split.test))
    cold_val, cold_test = indexed(split.cold_val), indexed(split.cold_test)
    n_users, n_items = len(user_ids), len(features.book_ids)

    item_counts = np.bincount(train["item"], minlength=n_items).astype(np.float32)
    user_counts = np.bincount(train["user"], minlength=n_users + 1).astype(np.float32)

    train_histories = build_histories(train, n_users, split.val_start, history_length, half_life_days)
    # At test time the validation period is the past: profiles may use it (no retraining needed for that).
    known = pd.concat([train, val], ignore_index=True)
    test_histories = build_histories(known, n_users, split.test_start, history_length, half_life_days)

    def eval_set(target: pd.DataFrame, seen: pd.DataFrame, histories: Histories) -> EvalSet:
        users = np.sort(target["user"].unique())
        return EvalSet(users, _grouped(target, users), _grouped(seen, users), histories)

    return TrainingData(
        features=features,
        user_ids=user_ids,
        train_users=train["user"].to_numpy(),
        train_items=train["item"].to_numpy(),
        item_counts=item_counts,
        user_counts=user_counts,
        train_histories=train_histories,
        val=eval_set(val, train, train_histories),
        cold_val=eval_set(cold_val, train, train_histories),
        test=eval_set(test, known, test_histories),
        cold_test=eval_set(cold_test, known, test_histories),
        split=split,
    )

"""The hybrid network (CLAUDE.md, "Рекомендаційна модель"), in Keras 3:

c_i   = ReLU(W_c [e_i ⊕ a_i ⊕ g_i ⊕ v_i] + b_c)                      content vector of a book
u_c   = Σ w_ui c_i / Σ w_ui,  w_ui = r_ui · exp(-λ(t0 - t_ui))        content profile of a user
s_c   = cos(u_c, c_i)
q_i   = q_i^id if n_i ≥ n_min else W_q c_i                           book vector for NeuMF
s_cf  = NeuMF(p_u, q_i)  (GMF ⊕ MLP, before the sigmoid)
α_ui  = σ(w1·log(1+n_u) + w2·log(1+n_i) + b_α)
ŷ_ui  = σ(z_ui),  z_ui = α_ui·s_cf + (1-α_ui)·γ·s_c

Index 0 of the user, publisher and language tables means "unknown" and always gives a zero vector (like
padding); author and genre slots padded with 0 are left out of their means.
"""

import contextlib
from collections.abc import Iterator
from dataclasses import asdict, dataclass

import keras
import numpy as np
from keras import layers, ops

from recsys.dataset.features import BookFeatures


@dataclass
class ModelConfig:
    n_items: int
    n_users: int  # including index 0, the unknown user
    text_dim: int
    n_authors: int
    n_genres: int
    n_publishers: int
    n_languages: int
    numeric_dim: int
    author_slots: int = 3  # authors per book (padded)
    genre_slots: int = 4  # genres per book (padded)
    content_dim: int = 64
    latent_dim: int = 64
    author_dim: int = 32
    genre_dim: int = 16
    publisher_dim: int = 16
    language_dim: int = 8
    mlp_dims: tuple[int, ...] = (64, 32)
    n_min: int = 5

    def to_dict(self) -> dict:
        return asdict(self)


@dataclass
class Scores:
    z: object  # combined logit
    alpha: object  # weight of the collaborative component
    s_cf: object
    s_c: object


@contextlib.contextmanager
def inference_mode() -> Iterator[None]:
    """No gradient bookkeeping while scoring. Keras has no backend-independent switch for this when layers are
    called directly (outside predict()); on the PyTorch backend it is torch.no_grad()."""
    if keras.backend.backend() == "torch":
        import torch

        with torch.no_grad():
            yield
    else:
        yield


def _masked_mean(embedded, ids):
    """Mean of the embedded ids, ignoring padding (id 0). embedded: [..., slots, d]; ids: [..., slots]."""
    weights = ops.expand_dims(ops.cast(ops.not_equal(ids, 0), embedded.dtype), -1)
    return ops.sum(embedded * weights, axis=-2) / ops.maximum(ops.sum(weights, axis=-2), 1.0)


def _zero_unknown(embedding: layers.Embedding, ids):
    """Lookup where id 0 (unknown) gives a zero vector, so it adds nothing and is never trained."""
    vectors = embedding(ids)
    return vectors * ops.expand_dims(ops.cast(ops.not_equal(ids, 0), vectors.dtype), -1)


def _cosine(a, b, eps: float = 1e-8):
    dot = ops.sum(a * b, axis=-1)
    return dot / ops.maximum(ops.norm(a, axis=-1) * ops.norm(b, axis=-1), eps)


class HybridModel(keras.Model):
    """Content component + NeuMF + the adaptive gate. Used through its methods (content, user_profile,
    score, score_catalog) rather than call(): training wraps it in recsys.model.train.TrainingModel."""

    def __init__(self, config: ModelConfig, seed: int = 0, **kwargs) -> None:
        super().__init__(**kwargs)
        self.config = c = config

        # Book features and counts: non-trainable weights, so they are saved with the model.
        def feature(name: str, shape: tuple, dtype: str = "float32"):
            return self.add_weight(name=name, shape=shape, dtype=dtype, initializer="zeros", trainable=False)

        self.text = feature("text", (c.n_items, c.text_dim))
        self.authors = feature("authors", (c.n_items, c.author_slots), "int32")
        self.genres = feature("genres", (c.n_items, c.genre_slots), "int32")
        self.publisher = feature("publisher", (c.n_items,), "int32")
        self.language = feature("language", (c.n_items,), "int32")
        self.numeric = feature("numeric", (c.n_items, c.numeric_dim))
        self.item_counts = feature("item_counts", (c.n_items,))
        self.user_counts = feature("user_counts", (c.n_users,))

        # Small initial vectors. For metadata this matters: with large initial vectors an author vector would
        # outweigh the unit-length text embedding, and authors or publishers that occur only in new books
        # (never trained) would add pure noise to exactly the vectors cold start relies on.
        small = keras.initializers.RandomNormal(stddev=0.01, seed=seed)

        # Content component.
        self.author_emb = layers.Embedding(c.n_authors, c.author_dim, embeddings_initializer=small)
        self.genre_emb = layers.Embedding(c.n_genres, c.genre_dim, embeddings_initializer=small)
        self.publisher_emb = layers.Embedding(c.n_publishers, c.publisher_dim, embeddings_initializer=small)
        self.language_emb = layers.Embedding(c.n_languages, c.language_dim, embeddings_initializer=small)
        self.content_proj = layers.Dense(c.content_dim, activation="relu")

        # Collaborative component: NeuMF with separate GMF and MLP embeddings.
        self.user_gmf = layers.Embedding(c.n_users, c.latent_dim, embeddings_initializer=small)
        self.user_mlp = layers.Embedding(c.n_users, c.latent_dim, embeddings_initializer=small)
        self.item_gmf = layers.Embedding(c.n_items, c.latent_dim, embeddings_initializer=small)
        self.item_mlp = layers.Embedding(c.n_items, c.latent_dim, embeddings_initializer=small)
        self.cold_gmf = layers.Dense(c.latent_dim, use_bias=False)  # W_q (GMF part)
        self.cold_mlp = layers.Dense(c.latent_dim, use_bias=False)  # W_q (MLP part)
        # A list rather than keras.Sequential: a built Sequential fixes the input rank, while the MLP is
        # applied to [batch, books, features].
        self.mlp_layers = [layers.Dense(dim, activation="relu") for dim in c.mlp_dims]
        self.neumf_out = layers.Dense(1)

        # Combination.
        self.alpha_weights = self.add_weight(  # w1 (user data), w2 (book data)
            name="alpha_weights", shape=(2,), initializer=keras.initializers.Constant(0.5)
        )
        self.alpha_bias = self.add_weight(  # little data → content dominates at the start
            name="alpha_bias", shape=(), initializer=keras.initializers.Constant(-2.0)
        )
        self.gamma = self.add_weight(  # scales the cosine [-1, 1] to logit range
            name="gamma", shape=(), initializer=keras.initializers.Constant(5.0)
        )
        self.seed_generator = keras.random.SeedGenerator(seed)

        # Create every weight now (Keras otherwise creates them on first use), so saved weights can be loaded
        # into a fresh model.
        input_dim = c.text_dim + c.author_dim + c.genre_dim + c.publisher_dim + c.language_dim + c.numeric_dim
        for embedding in (
            self.author_emb, self.genre_emb, self.publisher_emb, self.language_emb,
            self.user_gmf, self.user_mlp, self.item_gmf, self.item_mlp,
        ):  # fmt: skip
            embedding.build()
        self.content_proj.build((None, input_dim))
        self.cold_gmf.build((None, c.content_dim))
        self.cold_mlp.build((None, c.content_dim))
        width = 2 * c.latent_dim
        for layer, dim in zip(self.mlp_layers, c.mlp_dims, strict=True):
            layer.build((None, width))
            width = dim
        self.neumf_out.build((None, c.latent_dim + c.mlp_dims[-1]))
        self.built = True

    def load_book_features(self, features: BookFeatures, text: np.ndarray, item_counts: np.ndarray) -> None:
        self.text.assign(text.astype(np.float32))
        self.authors.assign(features.authors.astype(np.int32))
        self.genres.assign(features.genres.astype(np.int32))
        self.publisher.assign(features.publisher.astype(np.int32))
        self.language.assign(features.language.astype(np.int32))
        self.numeric.assign(features.numeric.astype(np.float32))
        self.item_counts.assign(item_counts.astype(np.float32))

    def load_user_counts(self, user_counts: np.ndarray) -> None:
        self.user_counts.assign(user_counts.astype(np.float32))

    # --- content component -------------------------------------------------------------------------------

    def content(self, items=None, metadata_dropout: float = 0.0):
        """c_i for the given item indices (any shape), or for the whole catalog.

        metadata_dropout (training only): per book, the probability of hiding its author, publisher and
        numeric features. Without it the network learns combinations of them that identify individual training
        books (an author with one book is effectively an id), and those vectors don't carry over to new books.
        """
        features = (self.text, self.authors, self.genres, self.publisher, self.language, self.numeric)
        if items is not None:
            features = tuple(ops.take(f, items, axis=0) for f in features)
        return self.content_of(*features, metadata_dropout=metadata_dropout)

    def content_of(self, text, authors, genres, publisher, language, numeric, metadata_dropout: float = 0.0):
        """c_i from book features; also used at inference for books added after training."""
        author = _masked_mean(self.author_emb(authors), authors)
        publisher_vector = _zero_unknown(self.publisher_emb, publisher)
        numeric = ops.cast(numeric, "float32")
        if metadata_dropout > 0:
            random = keras.random.uniform(ops.shape(author)[:-1], seed=self.seed_generator)
            keep = ops.expand_dims(ops.cast(random >= metadata_dropout, "float32"), -1)
            author, publisher_vector, numeric = author * keep, publisher_vector * keep, numeric * keep
        parts = [
            ops.cast(text, "float32"),
            author,
            _masked_mean(self.genre_emb(genres), genres),
            publisher_vector,
            _zero_unknown(self.language_emb, language),
            numeric,
        ]
        return self.content_proj(ops.concatenate(parts, axis=-1))

    @staticmethod
    def user_profile(content, history, weights):
        """u_c: decay-weighted mean of the content vectors of the user's history (zero if empty).
        content: [items, d]; history: [batch, length] item indices (-1 = none); weights: [batch, length]."""
        vectors = ops.take(content, ops.maximum(history, 0), axis=0)
        weights = ops.cast(weights, "float32") * ops.cast(history >= 0, "float32")
        total = ops.sum(weights, axis=-1, keepdims=True)
        return ops.sum(vectors * ops.expand_dims(weights, -1), axis=-2) / ops.maximum(total, 1e-8)

    # --- scoring -------------------------------------------------------------------------------------------

    def item_vectors(self, items, item_content, use_content):
        """q_i for GMF and MLP: the learned id vector, or the content projection W_q c_i for cold books."""
        cold = ops.expand_dims(use_content, -1)
        # Books added after training have no id vector (index past the table); they always use the content
        # projection, so any valid row can stand in for the discarded id lookup.
        known = ops.minimum(items, self.config.n_items - 1)
        q_gmf = ops.where(cold, self.cold_gmf(item_content), self.item_gmf(known))
        q_mlp = ops.where(cold, self.cold_mlp(item_content), self.item_mlp(known))
        return q_gmf, q_mlp

    def score(self, users, items, profile, content, force_cold=None, item_counts=None) -> Scores:
        """Scores of items [batch, m] for users [batch]. profile: u_c [batch, d]; content: c for all items.
        force_cold marks items to treat as new (DropoutNet-style training): content projection and n_i = 0.
        item_counts: n_i for a catalog that differs from the trained one (inference with new books)."""
        item_content = ops.take(content, items, axis=0)
        s_c = _cosine(ops.expand_dims(profile, 1), item_content)

        n_i = ops.take(self.item_counts if item_counts is None else item_counts, items, axis=0)
        use_content = n_i < self.config.n_min
        if force_cold is not None:
            use_content = ops.logical_or(use_content, force_cold)
            n_i = ops.where(force_cold, ops.zeros_like(n_i), n_i)
        q_gmf, q_mlp = self.item_vectors(items, item_content, use_content)

        p_gmf = ops.broadcast_to(ops.expand_dims(_zero_unknown(self.user_gmf, users), 1), ops.shape(q_gmf))
        p_mlp = ops.broadcast_to(ops.expand_dims(_zero_unknown(self.user_mlp, users), 1), ops.shape(q_mlp))
        hidden = ops.concatenate([p_mlp, q_mlp], axis=-1)
        for layer in self.mlp_layers:
            hidden = layer(hidden)
        s_cf = ops.squeeze(self.neumf_out(ops.concatenate([p_gmf * q_gmf, hidden], axis=-1)), -1)

        n_u = ops.expand_dims(ops.take(self.user_counts, users, axis=0), 1)
        alpha = ops.sigmoid(
            self.alpha_weights[0] * ops.log1p(n_u) + self.alpha_weights[1] * ops.log1p(n_i) + self.alpha_bias
        )
        z = alpha * s_cf + (1 - alpha) * self.gamma * s_c
        return Scores(z=z, alpha=alpha, s_cf=s_cf, s_c=s_c)

    def score_catalog(self, users, history, weights, content=None, chunk: int = 2048) -> np.ndarray:
        """z for every catalog item: [batch, items] as numpy. Same computation as score(), in item chunks."""
        with inference_mode():
            content = self.content() if content is None else content
            profile = self.user_profile(content, history, weights)
            n_items = ops.shape(content)[0]
            out = np.empty((len(users), n_items), dtype=np.float32)
            for start in range(0, n_items, chunk):
                items = np.arange(start, min(start + chunk, n_items))
                batch_items = np.broadcast_to(items, (len(users), len(items)))
                z = self.score(users, batch_items, profile, content).z
                out[:, start : start + len(items)] = ops.convert_to_numpy(z)
        return out

    def align_loss(self, content, items):
        """L_align: ‖q_i^id − W_q c_i‖² for books with n_i ≥ n_min (W_q learns to imitate learned vectors).
        items: 1-d item indices; the mean is over the warm ones (0 if there are none)."""
        warm = ops.cast(ops.take(self.item_counts, items, axis=0) >= self.config.n_min, "float32")
        item_content = ops.take(content, items, axis=0)
        gmf = ops.sum(ops.square(self.item_gmf(items) - self.cold_gmf(item_content)), axis=-1)
        mlp = ops.sum(ops.square(self.item_mlp(items) - self.cold_mlp(item_content)), axis=-1)
        return ops.sum((gmf + mlp) * warm) / ops.maximum(ops.sum(warm), 1.0)

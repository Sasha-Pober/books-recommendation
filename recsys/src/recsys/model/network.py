"""The hybrid network (CLAUDE.md, "Рекомендаційна модель"):

c_i   = ReLU(W_c [e_i ⊕ a_i ⊕ g_i ⊕ v_i] + b_c)                      content vector of a book
u_c   = Σ w_ui c_i / Σ w_ui,  w_ui = r_ui · exp(-λ(t0 - t_ui))        content profile of a user
s_c   = cos(u_c, c_i)
q_i   = q_i^id if n_i ≥ n_min else W_q c_i                           book vector for NeuMF
s_cf  = NeuMF(p_u, q_i)  (GMF ⊕ MLP, before the sigmoid)
α_ui  = σ(w1·log(1+n_u) + w2·log(1+n_i) + b_α)
ŷ_ui  = σ(z_ui),  z_ui = α_ui·s_cf + (1-α_ui)·γ·s_c
"""

from dataclasses import asdict, dataclass

import numpy as np
import torch
import torch.nn.functional as F
from torch import Tensor, nn

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


def _masked_mean(embedded: Tensor, mask: Tensor) -> Tensor:
    weights = mask.unsqueeze(-1).float()
    return (embedded * weights).sum(-2) / weights.sum(-2).clamp(min=1.0)


@dataclass
class Scores:
    z: Tensor  # combined logit
    alpha: Tensor  # weight of the collaborative component
    s_cf: Tensor
    s_c: Tensor


class HybridModel(nn.Module):
    def __init__(self, config: ModelConfig) -> None:
        super().__init__()
        self.config = config
        c = config

        # Book features: buffers, so they travel with the model (and new books can be appended).
        self.register_buffer("text", torch.zeros(c.n_items, c.text_dim))
        self.register_buffer("authors", torch.zeros(c.n_items, c.author_slots, dtype=torch.long))
        self.register_buffer("genres", torch.zeros(c.n_items, c.genre_slots, dtype=torch.long))
        self.register_buffer("publisher", torch.zeros(c.n_items, dtype=torch.long))
        self.register_buffer("language", torch.zeros(c.n_items, dtype=torch.long))
        self.register_buffer("numeric", torch.zeros(c.n_items, c.numeric_dim))
        self.register_buffer("item_counts", torch.zeros(c.n_items))
        self.register_buffer("user_counts", torch.zeros(c.n_users))

        # Content component.
        self.author_emb = nn.Embedding(c.n_authors, c.author_dim, padding_idx=0)
        self.genre_emb = nn.Embedding(c.n_genres, c.genre_dim, padding_idx=0)
        self.publisher_emb = nn.Embedding(c.n_publishers, c.publisher_dim, padding_idx=0)
        self.language_emb = nn.Embedding(c.n_languages, c.language_dim, padding_idx=0)
        input_dim = c.text_dim + c.author_dim + c.genre_dim + c.publisher_dim + c.language_dim + c.numeric_dim
        self.content_proj = nn.Linear(input_dim, c.content_dim)

        # Collaborative component: NeuMF with separate GMF and MLP embeddings.
        self.user_gmf = nn.Embedding(c.n_users, c.latent_dim, padding_idx=0)
        self.user_mlp = nn.Embedding(c.n_users, c.latent_dim, padding_idx=0)
        self.item_gmf = nn.Embedding(c.n_items, c.latent_dim)
        self.item_mlp = nn.Embedding(c.n_items, c.latent_dim)
        self.cold_gmf = nn.Linear(c.content_dim, c.latent_dim, bias=False)  # W_q (GMF part)
        self.cold_mlp = nn.Linear(c.content_dim, c.latent_dim, bias=False)  # W_q (MLP part)
        layers: list[nn.Module] = []
        width = 2 * c.latent_dim
        for dim in c.mlp_dims:
            layers += [nn.Linear(width, dim), nn.ReLU()]
            width = dim
        self.mlp = nn.Sequential(*layers)
        self.neumf_out = nn.Linear(c.latent_dim + width, 1)

        # Combination.
        self.alpha_weights = nn.Parameter(torch.tensor([0.5, 0.5]))  # w1 (user data), w2 (book data)
        self.alpha_bias = nn.Parameter(torch.tensor(-2.0))  # little data → content dominates at the start
        self.gamma = nn.Parameter(torch.tensor(5.0))  # scales the cosine [-1, 1] to logit range

        # Small initial vectors. For metadata this matters: with PyTorch's default N(0, 1) an author vector
        # (norm ≈ 5.7) would outweigh the unit-length text embedding, and authors or publishers that occur
        # only in new books (never trained) would add pure noise to exactly the vectors cold start relies on.
        for embedding in (
            self.author_emb, self.genre_emb, self.publisher_emb, self.language_emb,
            self.user_gmf, self.user_mlp, self.item_gmf, self.item_mlp,
        ):  # fmt: skip
            nn.init.normal_(embedding.weight, std=0.01)
            if embedding.padding_idx is not None:
                embedding.weight.data[embedding.padding_idx].zero_()

    def load_book_features(self, features: BookFeatures, text: np.ndarray, item_counts: np.ndarray) -> None:
        self.text = torch.as_tensor(text, dtype=torch.float32)
        self.authors = torch.as_tensor(features.authors)
        self.genres = torch.as_tensor(features.genres)
        self.publisher = torch.as_tensor(features.publisher)
        self.language = torch.as_tensor(features.language)
        self.numeric = torch.as_tensor(features.numeric)
        self.item_counts = torch.as_tensor(item_counts, dtype=torch.float32)

    def load_user_counts(self, user_counts: np.ndarray) -> None:
        self.user_counts = torch.as_tensor(user_counts, dtype=torch.float32)

    # --- content component -------------------------------------------------------------------------------

    def content(self, items: Tensor | None = None, metadata_dropout: float = 0.0) -> Tensor:
        """c_i for the given item indices (any shape), or for the whole catalog.

        metadata_dropout (training only): per book, the probability of hiding its author, publisher and
        numeric features. Without it the network learns combinations of them that identify individual training
        books (an author with one book is effectively an id), and those vectors don't carry over to new books.
        """
        index = slice(None) if items is None else items
        authors, genres = self.authors[index], self.genres[index]
        author = _masked_mean(self.author_emb(authors), authors != 0)
        publisher = self.publisher_emb(self.publisher[index])
        numeric = self.numeric[index]
        if metadata_dropout > 0:
            keep = (
                (torch.rand(author.shape[:-1], device=author.device) >= metadata_dropout)
                .unsqueeze(-1)
                .float()
            )
            author, publisher, numeric = author * keep, publisher * keep, numeric * keep
        parts = [
            self.text[index],
            author,
            _masked_mean(self.genre_emb(genres), genres != 0),
            publisher,
            self.language_emb(self.language[index]),
            numeric,
        ]
        return F.relu(self.content_proj(torch.cat(parts, dim=-1)))

    @staticmethod
    def user_profile(content: Tensor, history: Tensor, weights: Tensor) -> Tensor:
        """u_c: decay-weighted mean of the content vectors of the user's history (zero if empty).
        content: [items, d]; history: [batch, length] item indices (-1 = none); weights: [batch, length]."""
        vectors = content[history.clamp(min=0)]
        weights = weights * (history >= 0)
        total = weights.sum(-1, keepdim=True)
        return (vectors * weights.unsqueeze(-1)).sum(-2) / total.clamp(min=1e-8)

    # --- scoring -------------------------------------------------------------------------------------------

    def item_vectors(self, items: Tensor, item_content: Tensor, use_content: Tensor) -> tuple[Tensor, Tensor]:
        """q_i for GMF and MLP: the learned id vector, or the content projection W_q c_i for cold books."""
        cold = use_content.unsqueeze(-1)
        q_gmf = torch.where(cold, self.cold_gmf(item_content), self.item_gmf(items))
        q_mlp = torch.where(cold, self.cold_mlp(item_content), self.item_mlp(items))
        return q_gmf, q_mlp

    def score(
        self,
        users: Tensor,
        items: Tensor,
        profile: Tensor,
        content: Tensor,
        force_cold: Tensor | None = None,
    ) -> Scores:
        """Scores of items [batch, m] for users [batch]. profile: u_c [batch, d]; content: c for all items.
        force_cold marks items to treat as new (DropoutNet-style training): content projection and n_i = 0."""
        item_content = content[items]
        s_c = F.cosine_similarity(profile.unsqueeze(1), item_content, dim=-1, eps=1e-8)

        n_i = self.item_counts[items]
        use_content = n_i < self.config.n_min
        if force_cold is not None:
            use_content = use_content | force_cold
            n_i = torch.where(force_cold, torch.zeros_like(n_i), n_i)
        q_gmf, q_mlp = self.item_vectors(items, item_content, use_content)

        p_gmf = self.user_gmf(users).unsqueeze(1).expand_as(q_gmf)
        p_mlp = self.user_mlp(users).unsqueeze(1).expand_as(q_mlp)
        hidden = self.mlp(torch.cat([p_mlp, q_mlp], dim=-1))
        s_cf = self.neumf_out(torch.cat([p_gmf * q_gmf, hidden], dim=-1)).squeeze(-1)

        n_u = self.user_counts[users].unsqueeze(1)
        alpha = torch.sigmoid(
            self.alpha_weights[0] * torch.log1p(n_u)
            + self.alpha_weights[1] * torch.log1p(n_i)
            + self.alpha_bias
        )
        z = alpha * s_cf + (1 - alpha) * self.gamma * s_c
        return Scores(z=z, alpha=alpha, s_cf=s_cf, s_c=s_c)

    @torch.no_grad()
    def score_catalog(
        self,
        users: Tensor,
        history: Tensor,
        weights: Tensor,
        content: Tensor | None = None,
        chunk: int = 2048,
    ) -> Tensor:
        """z for every catalog item: [batch, items]. Same computation as score(), in item chunks."""
        content = self.content() if content is None else content
        profile = self.user_profile(content, history, weights)
        n_items = content.shape[0]
        out = torch.empty(users.shape[0], n_items, device=content.device)
        for start in range(0, n_items, chunk):
            items = torch.arange(start, min(start + chunk, n_items), device=content.device)
            out[:, start : start + len(items)] = self.score(
                users, items.unsqueeze(0).expand(users.shape[0], -1), profile, content
            ).z
        return out

    def align_loss(self, content: Tensor, items: Tensor) -> Tensor:
        """L_align: ‖q_i^id − W_q c_i‖² for books with n_i ≥ n_min (W_q learns to imitate learned vectors)."""
        items = items[self.item_counts[items] >= self.config.n_min]
        if items.numel() == 0:
            return content.new_zeros(())
        item_content = content[items]
        gmf = (self.item_gmf(items) - self.cold_gmf(item_content)).pow(2).sum(-1)
        mlp = (self.item_mlp(items) - self.cold_mlp(item_content)).pow(2).sum(-1)
        return (gmf + mlp).mean()

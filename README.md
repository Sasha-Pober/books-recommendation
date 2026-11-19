# Books Recommendation

A book store with a neural-network-based recommendation system.

| Part | Tech | Status |
|---|---|---|
| `backend/` | C# / ASP.NET Core 10, EF Core, PostgreSQL, Redis | ✅ store API, unit + integration tests |
| `frontend/` | TypeScript / Angular 22 / Angular Material | ✅ store: sign-in, catalog, book page, recommendations, cart, orders, ratings; 🚧 admin |
| `recsys/` | Python / FastAPI / Keras 3 (PyTorch backend) recommendation service | ✅ data import, training, model import, inference; awaiting the first trained model |

## Backend architecture

Onion architecture: every layer depends only on layers closer to the core, and `Domain` depends on nothing.

```
Api ──▶ Services ──▶ Presentation ──▶ Domain
 └────▶ Infrastructure ─────────────▶ Domain
```

```
backend/
├── src/
│   ├── BookStore.Domain/          entities, repository interfaces, IUnitOfWork, IRecommendationEngine, business exceptions
│   ├── BookStore.Presentation/    DTOs and request/response contracts
│   ├── BookStore.Services/        business logic (service interfaces + implementations), baseline recommendation engine
│   ├── BookStore.Infrastructure/  EF Core + PostgreSQL: DbContext (unit of work), repositories, migrations, seeding
│   └── BookStore.Api/             controllers, error handling (ProblemDetails), OpenAPI, health checks, DI composition
└── tests/
    ├── BookStore.UnitTests/         business logic with mocked repositories; no database, no Docker
    └── BookStore.IntegrationTests/  real PostgreSQL (Testcontainers) and the whole API over HTTP
```

`Services` has no EF Core dependency; it works only through the repository interfaces from `Domain`.

### Data model

PostgreSQL 17 with pgvector. Table names are snake_case.

- **Catalog:** `books`, `authors`, `book_authors`, `genres` (hierarchy via `parent_id`), `book_genres`, `publishers`, `series`.
- **Users:** `users`, `roles` and the other ASP.NET Core Identity tables. They live in Infrastructure (`AppUser`), and domain entities reference users by `UserId` only.
- **Shopping:** `cart_items`, `wishlist_items`, `orders`, `order_items` (`order_items` stores the price at the moment of purchase).
- **Feedback:** `ratings` (1–5 stars plus an optional review) and `interactions`, the event log. Each interaction has a weight: view 0.2, wishlist 0.5, cart 0.7, purchase or a 4–5 rating 1.0.
- **Recommendations:**
  - `book_embeddings` (`vector(384)`), written by the Python pipeline;
  - `model_versions`, where at most one version is active;
  - `recommendation_logs`, used for CTR and A/B tests.

`books.search_vector` is a generated `tsvector` column with a GIN index. It is built from the title and annotation using the `ukrainian` text search configuration, which the initial migration creates. That configuration needs the hunspell `uk_UA` dictionary, which is baked into the custom PostgreSQL image in `infra/postgres/`.

### Catalog

`GET /api/books` combines search, filters, sorting and paging (`page`, `pageSize` ≤ 100):

- **`search`:** Ukrainian full-text search over the title (weight A) and annotation (weight B), using the `ukrainian` configuration, so «дюну» finds «Дюна». It accepts web-search syntax (`"exact phrase"`, `-word`, `or`). A substring match on the title and author names also catches partial words («дюн») and authors.
- **Filters:**
  - `genreId` includes subgenres;
  - `authorId`, `publisherId`, `seriesId`, `language`;
  - `minPrice`/`maxPrice`, `minYear`/`maxYear`, `inStockOnly`.
- **`sort`:** `Relevance` (the default when searching), `Title` (the default otherwise), `PriceAscending`, `PriceDescending`, `Newest`, `Year`, `Rating`. Books without a year or without ratings go last. `Rating` is a Bayesian average, (sum + 10·m) / (count + 10) with m the mean of all ratings, so a book with one 5-star rating doesn't outrank one with hundreds of 4.5s.

**Performance.** These were measured on the imported Goodreads catalog (36k books, 980k ratings), p50 through the API:

| | Before | After |
|---|---|---|
| Search (most queries) | ~0.8 s | 10–40 ms |
| Search with thousands of matches ("love", 5.7k) | ~0.8 s | ~140 ms |
| Sort by rating | ~3.5 s | ~160 ms |

What makes it fast:
- **Search:** it's a union of three indexed lookups:
  - full text (GIN on `search_vector`);
  - part of the title (trigram GIN on `books.title`);
  - part of an author's name (trigram GIN on `authors.full_name`, which needs `pg_trgm`).

  The previous single `OR` condition made PostgreSQL check every book.
- **Rating:** `books.rating_count` and `rating_sum` hold the counters, kept in the same transaction as every rating. The `CatalogPerformance` migration backfilled them, and `recsys import-goodreads` recomputes them. Book cards read them too, so a page needs no rating query.
- **Ukrainian dictionary:** PostgreSQL loads it per connection on first use, which takes ~0.5 s and ~64 MB of memory per connection. Every new connection loads it as it opens, and at startup the API opens `Minimum Pool Size` connections (4 in development and Compose), which the pool then keeps. So no user search waits for it. Above that number, a new connection pays the ~0.5 s when it opens.

Reference lists for the filters: `GET /api/genres` (flat, with `parentId`), `/api/authors`, `/api/publishers`, `/api/series`.

Catalog management needs the `Manager` or `Admin` role:
- `POST/PUT/DELETE /api/admin/books`;
- `POST /api/admin/authors|genres|publishers|series`.

A book that has been ordered can't be deleted (409). Set its stock to 0 instead.

### Shopping and ratings (Customer role)

| Endpoint | Purpose |
|---|---|
| `GET/POST/DELETE /api/cart`, `PUT/DELETE /api/cart/{bookId}` | Cart. Each change returns the whole cart; the quantity can't exceed the stock |
| `GET/POST /api/wishlist`, `DELETE /api/wishlist/{bookId}` | Wishlist (adding and removing are idempotent) |
| `POST /api/orders` | Places an order from the cart |
| `GET /api/orders`, `GET /api/orders/{id}` | The customer's own orders |
| `POST /api/books/{id}/ratings` | Rates a book 1–5 with an optional review; rating it again replaces the old one |
| `GET /api/books/{id}/ratings` | Reviews of a book (public) |

Placing an order runs in one transaction:
1. Stock is decremented with a conditional `UPDATE`, so two customers can't buy the last copy.
2. The order is created with each line's current price.
3. The cart is emptied and purchase events are recorded.

If any book runs short, nothing is saved and the API returns 409.

### Events

There are two paths into the `interactions` table:

- **Page views** go through `POST /api/events`, which takes up to 100 `View` events per request. The request only puts the events in an in-memory queue (a bounded `System.Threading.Channels` channel, 10 000 events by default) and returns 202. It doesn't touch the database. A background writer, `InteractionBatchWriter`, saves the events:
  - in batches of up to 500, or 1 second after the first event of a batch;
  - after dropping events for unknown books or deleted accounts, with one query per batch;
  - retrying temporary database errors up to 3 times; if a single event breaks a batch, the rest are saved one by one.

  When the queue is full the endpoint returns 503 with `Retry-After`. There's also a limit of 300 requests per minute per IP. On shutdown the writer stops accepting events and saves everything still queued. These settings are in the `Events` section of `appsettings.json`.
- **Cart, wishlist, purchase and rating events** are recorded by the server at the moment those actions happen, in the same transaction, so they can't be faked or counted twice.

**Trade-off:** queued page views live only in memory. If the process crashes, or the database stays unavailable through the retries, those views are lost. That's acceptable for implicit feedback, but it's why purchases and ratings don't use the queue.

### Authentication and roles

ASP.NET Core Identity stores the accounts. The API is stateless and uses JWT bearer tokens.

| Endpoint | Purpose |
|---|---|
| `POST /api/auth/register` | Creates a `Customer` account and signs it in |
| `POST /api/auth/login` | Returns an access token (JWT, 15 min) and sets the refresh token cookie (14 days) |
| `POST /api/auth/refresh` | Exchanges the refresh token cookie for a new access token and cookie |
| `POST /api/auth/logout` | Revokes the refresh token and deletes the cookie |
| `GET /api/auth/me` | Returns the current user |

- **The refresh token is only in a cookie:** `bookstore_refresh`, HttpOnly, `SameSite=Strict`, `Path=/api/auth`. Page scripts can't read it, so an XSS can't steal a long-lived session, and other sites can't make the browser send it. The response body carries only the access token, which the frontend keeps in memory.
- **Refresh tokens are single-use.** Only their SHA-256 hash is stored. If an already used token is presented again, every session of that user is revoked, because the token was probably stolen. A rejected refresh also deletes the cookie.
- **Login protection:** an account is locked for 15 minutes after 5 failed passwords. The auth endpoints are also limited to 20 requests per minute per IP.
- **Roles:**
  - guests (no token) can browse, record events and get recommendations;
  - `Customer` can use the cart, wishlist and orders, and rate books;
  - `Manager` (and `Admin`) manage the catalog under `/api/admin/...`.
- **Guest sessions:** every browser gets an HttpOnly `bookstore_session` cookie. Guest events are recorded under it. On login or registration they are attached to the account, and the cookie is renewed on login and logout. The SPA must send requests with credentials for the cookie to work.
- **Configuration:** the `Jwt` section in `appsettings.json`. `Jwt:SigningKey` (at least 32 characters) is required, and the API refuses to start without it. The development key lives in `appsettings.Development.json`; set a real one through the `Jwt__SigningKey` environment variable elsewhere.

### Recommendations

| Endpoint | Purpose |
|---|---|
| `GET /api/recommendations/home` | "Recommended for you": personalized when signed in; for guests, based on the session's views |
| `GET /api/books/{id}/similar` | "Similar books" on the book page |

Each item has a `reason`, a Ukrainian `explanation` (e.g. «Схожа на книги, які ви переглядали», «Бестселер»), and a `logId`. The list reports its `source` (`ml` or `baseline`) and the `modelVersionId`.

**Engines.** `RecommendationService` asks the primary `IRecommendationEngine` first.
- **Python service:** when `RecommendationService:BaseUrl` is set, the primary engine is `RecommendationServiceClient`, the client of the Python service.
  - **Timeout:** 80 ms per call.
  - **Circuit breaker:** after 50% failures among at least 10 calls in 30 s, the service isn't called for 15 s.
  - **No retries:** there's no time for them within the 100 ms budget.
- **Fallback:** if the call fails, times out or returns nothing, `BaselineRecommendationEngine` answers instead:
  - home: bestsellers (copies sold in the 90 days before the latest order, padded with books trending in the 30 days before the latest event) interleaved with new releases. The windows end at the latest activity rather than now, which is the same thing in a live store and still works on imported historical data;
  - similar books: shared authors and genres. Candidates are the same authors' books plus, per genre, either all of its books (genres of up to 2,000 books) or its 200 most rated in stock (larger ones, walked along `ix_books_rating_count`, since a genre like "Poetry" may cover the whole catalog). They're scored +2 for the same author and +1 per shared genre, ties going to the more rated book. That takes 2–25 ms in the database, where one query over every book sharing a genre took ~400 ms.
- **Without a `BaseUrl`,** the baseline is the only engine.

**Collections cache.** Bestsellers and new releases are precomputed every 10 minutes by `RecommendationCollectionRefresher` and kept in Redis (`ConnectionStrings:Redis`) for 1 hour.
- **Without Redis:** an in-memory cache is used instead.
- **Redis down:** reads count as cache misses, and the collections are computed from the database.

**After filtering:**
1. Books the user bought or rated, and out-of-stock books, are removed.
2. Every shown item is written to `recommendation_logs` (placement, position, model version). If logging fails, the recommendations are still returned.
3. When the user opens a recommended book, the frontend sends a `View` event with that item's `recommendationLogId`, and the log entry is marked `clicked`. Only a log shown to the same user and for the same book counts. CTR is computed from these logs.

### Model versions (Admin role)

- **`GET /api/admin/models`:** trained versions from `model_versions`, newest first, with their offline metrics as JSON.
- **`POST /api/admin/models/{id}/activate`:** switches recommendations to an imported model version and returns it.
  - 404 if the version, or its files in the service, are unknown.
  - 503 with `Retry-After` while the recommendation service is unavailable.
- **Who writes the data:** models are trained offline (Kaggle), and `recsys import-model` registers the version, its metrics and the book embeddings.

**Roles** (`Customer`, `Manager`, `Admin`) are created by the `SeedRoles` migration, so they exist in every environment. The development seeder only adds the demo accounts.

### Recommendation service contract (Python, FastAPI)

The API calls the service over the internal network with the `X-Service-Key: <RecommendationService:ServiceKey>` header. JSON is snake_case.

```
POST /v1/recommendations/home
{"user_id": "uuid | null", "session_id": "string", "count": 20, "exclude_book_ids": ["uuid"]}

POST /v1/recommendations/similar
{"book_id": "uuid", "count": 10, "exclude_book_ids": ["uuid"]}

200 → {"model_version_id": 3, "items": [{"book_id": "uuid", "score": 0.91, "reason": "similar_to_viewed"}]}

POST /v1/models/{model_version_id}/activate
200 → {"model_version_id": 3}    404 if the version or its files are unknown
```

- **`reason`:** one of `similar_to_viewed`, `liked_by_similar_readers`, `new_from_favorite_author`, `bestseller`, `trending`, `new_release`, `same_author`, `similar_content`. An unknown reason only removes the explanation; the book is kept.
- **`model_version_id`:** must exist in `model_versions`, otherwise the impressions aren't logged.
- **Errors:** any non-2xx status or a slow response triggers the fallback.

## Frontend (`frontend/`)

An Angular 22 SPA: standalone components, signals, zoneless change detection, Angular Material (Material 3) and Vitest. The interface is in Ukrainian, and dates and prices use the `uk` locale (₴).

- **Same origin as the API:**
  - in development, `ng serve` proxies `/api` to `http://localhost:5292` (`proxy.conf.json`);
  - in Docker, nginx serves the app and proxies `/api` to the API container (`nginx.conf`);
  - so there's no CORS, and the guest-session cookie needs no cross-site settings.
  - The browser never reaches the recommendation service.
- **API types:** generated from the API's OpenAPI document. `openapi.json` is committed, and `src/app/api/schema.ts` is generated from it; short aliases are in `src/app/api/models.ts`. After changing API contracts, with the API running in Development:

  ```bash
  npm run api:fetch    # saves openapi.json from http://localhost:5292/openapi/v1.json
  npm run api:types    # regenerates src/app/api/schema.ts
  ```
- **Behind nginx:** the API reads the client address from `X-Forwarded-For`, trusted only from `ReverseProxy:TrustedNetworks`, so the per-IP rate limits apply per user and not to nginx.

**Signing in** (`src/app/core/auth`):
- `AuthService` holds the user (a signal) and the access token, in memory only. On page load it restores the session with `/api/auth/refresh`. It only tries when `localStorage` says this browser has signed in, which is a hint, not a secret, so guests make no extra request.
- `authInterceptor` adds `Authorization: Bearer …` to `/api` requests, refreshing a token that's about to expire first. If the API still answers 401, it refreshes once and retries.
- **One refresh at a time:** refresh tokens are single-use and every tab shares the cookie, so two tabs refreshing at once would look like token theft and sign the user out everywhere. Refreshes are therefore serialized across tabs with a Web Lock, and concurrent requests within a tab share one refresh. Sign-in and sign-out reach other tabs through a `BroadcastChannel`.
- **Guards:** `authGuard` sends guests to `/login?returnUrl=…`; `roleGuard(...)` checks roles; `guestOnlyGuard` keeps signed-in users off the sign-in pages. Return links must stay inside the app.
- **Pages:** `/login`, `/register` (the password checklist mirrors Identity's rules), `/account`.

**Catalog** (`/catalog`, `src/app/catalog`, `src/app/pages/catalog`):
- **The URL holds the state:** `?search=&genre=&author=&publisher=&series=&lang=&priceFrom=&priceTo=&yearFrom=&yearTo=&inStock=1&sort=&page=&size=`. So results can be bookmarked and shared, and Back works. `catalog-query.ts` maps the URL to `/api/books` parameters, dropping malformed values (for example an id that isn't a GUID, or a page size above 100) instead of sending them.
- **Fetching:** the page loads with `httpResource`. A new query cancels the previous request, and the last results stay on screen, dimmed, while the next ones load.
- **Filters:** genre (as a tree), language (names from `Intl.DisplayNames`), price, year and availability. There are 15k authors and 8k publishers, too many for dropdowns, so author, publisher and series filters come from links on books and show as removable chips.
- **Default sort:** relevance when searching, rating otherwise. The toolbar search box shows the current search.
- **Covers:** the Goodreads image when there is one (56% of books); otherwise, or if it fails to load, a generated cover with the title and author.

**Home page** (`/`):
- **«Рекомендовано для вас»:** when the API's `source` is `ml`, the model's list is shown as one row, each book with its explanation. The model draws on a signed-in user's history, or on a guest's views in this browser.
- **Fallback:** with `source` `baseline` (a new visitor, or the recommendation service down), the same response is split into «Популярне зараз» (`Bestseller`/`Trending`) and «Новинки» (`NewRelease`).
- **Reloading:** the request waits until the session is restored, so a returning user doesn't first get a guest's list, and it reloads when the user signs in or out.
- **Also on the page:** a greeting and the top-level genres as shortcuts into the catalog.

**Book page** (`/books/:id`):
- **Details:** cover, rating, price and availability, publisher, series, language, year, pages, ISBN and annotation. Authors, genres, publisher and series link into the catalog filters.
- **«Схожі книги»:** a row of recommendations, each with its explanation. If this optional block fails it's simply left out, without the global error message (`SILENT_ERRORS`). `RecommendationRow` is shared with the home page.
- **Views:** opening a book records one `View` event (`InteractionEvents`, sent with `keepalive`, errors ignored). A recommendation card passes its `logId` in the navigation state, so the view also marks that impression clicked in `recommendation_logs`, which is the CTR measure for the A/B comparison.

**Shopping** (`src/app/shop`, customers only; guests who try are sent to sign in and brought back):
- **`CartStore` and `WishlistStore`:** signals loaded once per signed-in customer and cleared on sign-out. Every change returns the whole cart or wishlist from the API, which replaces the local copy, so the server stays the source of truth. The toolbar shows the cart badge and a wishlist link. Managers and admins have no cart.
- **Book page:** «Додати в кошик» (or «У кошику · N шт.») and the wishlist heart (`BuyButtons`).
- **`/cart`:** quantity steppers, limited to the stock and to 99 per book. A line short of stock is flagged and blocks checkout. «Оформити замовлення» (`POST /api/orders`) opens the new order with a thank-you note; a 409 (stock ran out meanwhile) refreshes the cart.
- **`/wishlist`:** move a book to the cart or remove it.
- **`/orders` and `/orders/:id`:** order history with statuses, and each order at the prices paid.
- **Ratings and reviews** (`BookReviews` on the book page): customers rate 1–5 stars with an optional review. `GET /api/books/{id}/ratings/mine` fills in their earlier rating so they can change it, and saving reloads the book's average. The list shows only written reviews (`GET /api/books/{id}/ratings?withTextOnly=true`), 5 at a time: the imported Goodreads ratings are scores only.
- **Events:** the server records the cart, wishlist, purchase and rating events itself, so the frontend reports only views.

**Running locally.** Node is managed with [fnm](https://github.com/Schniz/fnm); the version is in `.node-version`.

```bash
cd frontend
fnm use              # Node from .node-version (fnm install, the first time)
npm ci
npm start            # http://localhost:4200, with the API running on :5292
npm test             # Vitest
npm run format:check
```

## Recommendation service (`recsys/`)

A Python 3.12 FastAPI service, managed with [uv](https://docs.astral.sh/uv/). Only the API calls it, over the internal Docker network: the port isn't published, and every `/v1` request needs the `X-Service-Key` header.

```
recsys/
├── src/recsys/
│   ├── main.py          app factory, lifespan (database pool)
│   ├── config.py        RECSYS_* settings (database URL, service key, artifacts dir)
│   ├── security.py      X-Service-Key check
│   ├── db.py            psycopg connection pool to the store database
│   ├── recommender.py   Recommender protocol + registry of the active model
│   ├── api/             routes and the request/response schemas (the contract above)
│   ├── cli.py           `recsys serve | import-goodreads | export-dataset`
│   ├── goodreads/       import of the UCSD Goodreads data into the store database
│   ├── dataset/         export for training, r_ui and the time-based split, book feature encoding
│   └── model/           hybrid network, training, metrics, baselines, model bundle
├── notebooks/           train_kaggle.ipynb
└── tests/               pytest
```

**Without an imported model**, the recommendation endpoints answer 503 with `Retry-After`, and the API uses its own fallback. `GET /health` (no key needed) reports whether the database is reachable and which model version is loaded.

**Training workflow (offline).** Training takes long, so it isn't triggered from the store:

```
store DB ──recsys export-dataset──▶ parquet files ──upload──▶ Kaggle (private dataset) ─ train on GPU
store DB ◀──recsys import-model───── model bundle ◀──download────────────────────────────┘
```

The admin switches between imported versions with `POST /api/admin/models/{id}/activate`. The API forwards this to `POST /v1/models/{id}/activate` in the service, which loads that version.

**Dataset.** The UCSD Book Graph (Goodreads) genre subsets, Poetry by default. The data is for **academic use only and must not be redistributed**: it isn't committed, so download it yourself into `recsys/data/goodreads/`:

| File | URL |
|---|---|
| `goodreads_books_poetry.json.gz` | https://mcauleylab.ucsd.edu/public_datasets/gdrive/goodreads/byGenre/goodreads_books_poetry.json.gz |
| `goodreads_interactions_poetry.json.gz` | https://mcauleylab.ucsd.edu/public_datasets/gdrive/goodreads/byGenre/goodreads_interactions_poetry.json.gz |
| `goodreads_book_authors.json.gz` | https://mcauleylab.ucsd.edu/public_datasets/gdrive/goodreads/goodreads_book_authors.json.gz |
| `goodreads_book_genres_initial.json.gz` | https://mcauleylab.ucsd.edu/public_datasets/gdrive/goodreads/goodreads_book_genres_initial.json.gz |

When using the data, cite:
- Mengting Wan, Julian McAuley, "Item Recommendation on Monotonic Behavior Chains", RecSys'18.
- Mengting Wan, Rishabh Misra, Ndapa Nakashole, Julian McAuley, "Fine-Grained Spoiler Detection from Large-Scale Review Corpora", ACL'19.

**Import** (`uv run recsys import-goodreads`) writes the subset into the store database in one transaction, and can be repeated: ids are deterministic UUIDs, and the previous import's events are replaced.
- **Catalog:** books, authors, publishers and genres. Prices are synthetic (150–600, stable per book) and stock is 25.
- **Readers:** users with at least 5 shelved books, as Customer accounts without a password, so they can't sign in.
- **Events and ratings:** each shelf record becomes store events (see the table below), and ratings also go into `ratings`.

| Goodreads record | Store event | Weight | Time |
|---|---|---|---|
| shelved, not read ("to-read") | `AddToWishlist` | 0.5 | `date_added` |
| read | `Purchase` | 1.0 | `date_added` |
| rated 1–5 | `Rating` (+ row in `ratings`) | 1.0 for 4–5, else 0 | `date_updated` |

**Export** (`uv run recsys export-dataset`) writes one consistent snapshot to `data/export/<UTC time>/`: `interactions`, `books`, `book_authors`, `book_genres` and `genres` as parquet files, plus `meta.json`. Upload that folder to Kaggle as a **private** dataset.

**Training data** (`recsys.dataset`):
- **Interest r_ui:** the maximum weight per (user, book). The time t_ui is when that maximum was first reached.
- **Time-based split:** train / val / test, 80 / 10 / 10 by time. Val and test keep only users and books seen in train.
- **Cold-start test set:** known users with books that first appeared in the test period.
- **Book features:** authors, genres, publisher and language indices, plus standardized year and page count. The text (title + annotation) is embedded during training.

**Model** (`recsys.model`, following `CLAUDE.md`):
- **Content component:** the frozen `paraphrase-multilingual-MiniLM-L12-v2` text embedding (384-d, title + annotation) and author / genre / publisher / language embeddings and numeric features go through `c_i = ReLU(W_c[…])`. The user profile `u_c` is the decay-weighted mean (half-life 180 days) of `c_i` over the latest 50 interactions, and `s_c = cos(u_c, c_i)`.
- **Collaborative component:** NeuMF (GMF + MLP). A book with `n_i < 5` uses `W_q·c_i` instead of its id vector.
- **Combination:** `α_ui = σ(w1·log(1+n_u) + w2·log(1+n_i) + b)`, `ŷ = σ(α·s_cf + (1−α)·γ·s_c)`.
- **Loss:** `L_BPR + β·L_align + λ‖Θ‖²`. Adam, lr 1e-3, batch 1024 triples, 4 negatives, latent dim 64, early stopping on validation NDCG@10.
- **Regularization:**
  - **DropoutNet:** a book is treated as new with p = 0.2.
  - **Metadata dropout:** a book's author, publisher and numbers are hidden with p = 0.3. Without it the content tower learns feature combinations that identify individual training books and generalizes poorly to new ones.
  - **Small initialization:** metadata embeddings start small (std 0.01), so authors that only occur in new books add no noise.
- **Evaluation:** ranking over the full catalog, with known books excluded, on a warm test set and a cold-start test set (books first seen in the test period).
  - **Metrics:** P/R/HR/MRR/NDCG@{10,20}, Coverage, ILD and Novelty.
  - **Baselines:** popularity, TF-IDF and BPR matrix factorization.

**Training.** On Kaggle, open `notebooks/train_kaggle.ipynb` (instructions inside). It needs two private datasets: the export folder, and the wheel from `uv build --wheel`. To train locally instead:

```bash
uv run --extra cpu recsys train --data data/export/<UTC time> --out artifacts/bundles
```

The result is a bundle folder: Keras weights (`model.weights.h5`), ids, vocabularies, text embeddings, `metrics.json` and `training.json`. On a CPU an epoch takes about 10 minutes for the Poetry subset, plus validation.

**Framework.** The model is written in **Keras 3** (`recsys/model/network.py`, `train.py`) and runs on the **PyTorch backend**. sentence-transformers, which produces the text embeddings, needs PyTorch anyway, so only one framework is installed.
- **Training:** an idiomatic `model.fit(...)`:
  - `TripleBatches` (a `keras.utils.PyDataset`) samples the 4 negatives, the leave-one-out histories and the DropoutNet mask per batch;
  - `TrainingModel.compute_loss` returns L_BPR + β·L_align + λ‖Θ‖²;
  - `ValidationCallback` computes the validation NDCG@10, keeps the best weights and stops early.
- **Backend:** the model code uses only `keras.ops`, so another backend can be used by setting `KERAS_BACKEND` before Keras is first imported. The PyTorch backend is the tested one. Bundles from before the switch (format 1, PyTorch weights) can't be imported; retrain instead.

**Importing a trained model:**

```bash
cd recsys
uv run --extra cpu recsys import-model ~/Downloads/model.zip    # or a bundle folder
```

- **Checks:** that the bundle loads, and how many of its books still exist in the store. Books added after training are handled as cold start.
- **Files:** copies the bundle to `recsys/artifacts/models/<name>/`, which Docker Compose mounts read-only into the service.
- **Database:** writes `model_versions` (with all metrics) and the book text vectors to `book_embeddings` (`vector(384)`).
- **Activation:** `--activate auto`, the default, activates the model only if there is no active model or the new one has a higher test NDCG@10. `--activate yes` / `--activate no` override that.

**Activation** is the `is_active` flag in `model_versions`. The service loads the active version at startup and checks the flag every 30 s (`RECSYS_MODEL_POLL_SECONDS`), so a model activated by the import command is picked up without a restart.

`POST /api/admin/models/{id}/activate` goes through the service instead: it loads the model first and only then switches the flag. If loading fails, the previous model stays active.

**Inference** (`recsys/inference.py`) follows the spec's steps, on live store data.

"Recommended for you":
1. **Profile:** the user's recent events, or for a guest the session's, are read from PostgreSQL (up to 500). This gives u_c, with the same rules as in training: the maximum weight per book, the newest 50 books, and a half-life decay. Ages are measured from the newest event, so old imported data doesn't decay to zero. p_u is used if training knew the user.
2. **No history and an unknown user:** an empty list, so the API shows its bestsellers and new releases.
3. **Candidates:**
   - **Content:** 200 nearest to u_c (FAISS, content vectors c_i).
   - **Collaborative:** 200 nearest to p_u (FAISS, item vectors q_i).
   - **New books:** the popular new books.

   Excluded (bought, rated) and out-of-stock books are removed.
4. **Ranking and MMR:** candidates are ranked by ŷ_ui, then re-ranked with MMR (μ = 0.8) for diversity.
5. **Explanation:** "new from a favourite author" if the book is new and shares an author with the history. Otherwise the dominant component decides: α·s_cf gives `liked_by_similar_readers`, and (1−α)·γ·s_c gives `similar_to_viewed`.

"Similar books": content similarity to the book, averaged with collaborative similarity when both books have at least `n_min` interactions. The explanation is `same_author`, `liked_by_similar_readers` or `similar_content`.

**Books added after training** take part as cold-start items:
- **Encoding:** they're encoded with the model's vocabularies, and their text is embedded with the same frozen sentence-transformers model. In Docker it's downloaded once into the `recsys-hf-cache` volume.
- **Collaborative part:** NeuMF uses their content projection W_q c_i.

The catalog (books, stock, authors, the popular new books) is reloaded every 5 minutes (`RECSYS_CATALOG_REFRESH_SECONDS`).

**Latency** on the Poetry catalog (36k books, 117k readers, CPU), excluding the history query:

| | p50 | p95 |
|---|---|---|
| Recommended for you (20 books) | 4.6 ms | 6.8 ms |
| Similar books (10 books) | 1.3 ms | 1.9 ms |

**Running locally:**

```bash
cd recsys
uv sync --extra cpu          # .venv with Python 3.12, Keras and the CPU build of PyTorch (or --extra cu128 for a GPU)
cp .env.example .env         # database URL + service key
uv run recsys serve          # http://localhost:8000/docs
uv run --extra cpu pytest    # tests
uv run ruff check . && uv run ruff format --check .
```

## Running locally

Requirements: Docker. The .NET 10 SDK is needed only to run the API outside Docker.

### Everything in Docker

```bash
docker compose up -d --build   # PostgreSQL + Redis + recommendation service + API + frontend (http://localhost:4200)
```

The API container runs in `Development` mode. On startup it applies migrations and seeds roles, demo accounts and a demo Ukrainian catalog. Database credentials come from `.env` (copy `.env.example`), or default to `bookstore`.

Demo accounts (development only):

| Email | Password | Role |
|---|---|---|
| admin@bookstore.local | Admin123! | Admin |
| manager@bookstore.local | Manager123! | Manager |
| customer@bookstore.local | Customer123! | Customer |

### API from the IDE / CLI, database in Docker

```bash
docker compose up -d postgres
cd backend
dotnet run --project src/BookStore.Api
```

Both ways expose the API on port 5292:

- API reference (Scalar): http://localhost:5292/scalar
- OpenAPI document: http://localhost:5292/openapi/v1.json
- Health: http://localhost:5292/health
- Sample requests: `backend/src/BookStore.Api/BookStore.Api.http`

Stop the `api` container before running the API locally, since both use port 5292. To reset the database: `docker compose down -v`.

Check the Ukrainian full-text search configuration:

```bash
docker compose exec postgres psql -U bookstore -d bookstore \
  -c "SELECT to_tsvector('ukrainian', 'Пол Атрід бореться за пустельну планету')"
```

### Tests

```bash
cd backend
dotnet test tests/BookStore.UnitTests          # fast, no Docker
dotnet test tests/BookStore.IntegrationTests   # needs Docker running
dotnet test                                    # both
```

**What the integration tests do:**
1. Build the PostgreSQL image from `infra/postgres/` (pgvector plus the Ukrainian dictionary). The first run takes a minute or two; later runs use Docker's cache.
2. Start one container for the whole run and apply the migrations.
3. Empty the tables before each test.
4. Remove the container afterwards; Testcontainers also starts its own short-lived `ryuk` container for this cleanup.

**What they cover:**
- **Migrations:** the Ukrainian search configuration and the seeded roles.
- **Constraints:** the rating check, `vector(384)`, and the single active model.
- **Full-text search:** word forms, ranking, partial words, authors, excluded words.
- **Filters and sorting.**
- **Concurrency:** concurrent stock reservations, and order rollback.
- **Events:** the event writer and click marking, plus merging guest events on sign-in.
- **Tokens:** refresh-token rotation and reuse detection.
- **Over HTTP** (`WebApplicationFactory`): registration and login, admin model endpoints, and the recommendation and click loop.

With Docker Desktop on Linux, if Testcontainers can't find Docker, point it at the Desktop socket:
`export DOCKER_HOST=unix://$HOME/.docker/desktop/docker.sock`.

### Migrations

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> -p src/BookStore.Infrastructure -s src/BookStore.Api -o Persistence/Migrations
```

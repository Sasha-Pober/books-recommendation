# Books Recommendation

A book store with a neural-network-based recommendation system.

| Part | Tech | Status |
|---|---|---|
| `backend/` | C# / ASP.NET Core 10, EF Core, PostgreSQL | ✅ basic API |
| `frontend/` | TypeScript / Angular | planned |
| `ml/` | Python / PyTorch recommendation service | planned |

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
    └── BookStore.UnitTests/       service tests (NSubstitute mocks) and repository query tests
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
- **`sort`:** `Relevance` (the default when searching), `Title` (the default otherwise), `PriceAscending`, `PriceDescending`, `Newest`, `Year`, `Rating`. Books without a year or without ratings go last.

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
| `POST /api/auth/register` | Creates a `Customer` account and returns tokens |
| `POST /api/auth/login` | Returns an access token (JWT, 15 min) and a refresh token (14 days) |
| `POST /api/auth/refresh` | Exchanges a refresh token for a new pair |
| `POST /api/auth/logout` | Revokes the refresh token |
| `GET /api/auth/me` | Returns the current user |

- **Refresh tokens are single-use.** Only their SHA-256 hash is stored. If an already used token is presented again, every session of that user is revoked, because the token was probably stolen.
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
  - home: bestsellers (copies sold in 90 days, padded with books trending by events) interleaved with new releases;
  - similar books: shared authors and genres.
- **Without a `BaseUrl`,** the baseline is the only engine.

**Collections cache.** Bestsellers and new releases are precomputed every 10 minutes by `RecommendationCollectionRefresher` and kept in Redis (`ConnectionStrings:Redis`) for 1 hour.
- **Without Redis:** an in-memory cache is used instead.
- **Redis down:** reads count as cache misses, and the collections are computed from the database.

**After filtering:**
1. Books the user bought or rated, and out-of-stock books, are removed.
2. Every shown item is written to `recommendation_logs` (placement, position, model version). If logging fails, the recommendations are still returned.
3. When the user opens a recommended book, the frontend sends a `View` event with that item's `recommendationLogId`, and the log entry is marked `clicked`. Only a log shown to the same user and for the same book counts. CTR is computed from these logs.

### Recommendation service contract (Python, FastAPI)

The API calls the service over the internal network with the `X-Service-Key: <RecommendationService:ServiceKey>` header. JSON is snake_case.

```
POST /v1/recommendations/home
{"user_id": "uuid | null", "session_id": "string", "count": 20, "exclude_book_ids": ["uuid"]}

POST /v1/recommendations/similar
{"book_id": "uuid", "count": 10, "exclude_book_ids": ["uuid"]}

200 → {"model_version_id": 3, "items": [{"book_id": "uuid", "score": 0.91, "reason": "similar_to_viewed"}]}
```

- **`reason`:** one of `similar_to_viewed`, `liked_by_similar_readers`, `new_from_favorite_author`, `bestseller`, `trending`, `new_release`, `same_author`, `similar_content`. An unknown reason only removes the explanation; the book is kept.
- **`model_version_id`:** must exist in `model_versions`, otherwise the impressions aren't logged.
- **Errors:** any non-2xx status or a slow response triggers the fallback.

## Running locally

Requirements: Docker. The .NET 10 SDK is needed only to run the API outside Docker.

### Everything in Docker

```bash
docker compose up -d --build   # PostgreSQL + Redis + API
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
cd backend && dotnet test
```

### Migrations

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> -p src/BookStore.Infrastructure -s src/BookStore.Api -o Persistence/Migrations
```

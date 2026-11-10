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
  - `Customer` can rate books;
  - `Manager` (and `Admin`) can create, edit and delete books, authors and genres.
- **Guest sessions:** every browser gets an HttpOnly `bookstore_session` cookie. Guest events are recorded under it. On login or registration they are attached to the account, and the cookie is renewed on login and logout. The SPA must send requests with credentials for the cookie to work.
- **Configuration:** the `Jwt` section in `appsettings.json`. `Jwt:SigningKey` (at least 32 characters) is required, and the API refuses to start without it. The development key lives in `appsettings.Development.json`; set a real one through the `Jwt__SigningKey` environment variable elsewhere.

### Recommendations

`IRecommendationEngine` returns ranked book ids. `RecommendationService` sits in front of it and:

- removes books the user already bought or rated, and books that are out of stock;
- enriches the ids with book data;
- falls back to `BaselineRecommendationEngine` (popularity plus author/genre similarity) if the primary engine fails or returns nothing.

For now the baseline engine is the primary engine. The Python neural-network service will plug in as another `IRecommendationEngine` implementation.

## Running locally

Requirements: Docker. The .NET 10 SDK is needed only to run the API outside Docker.

### Everything in Docker

```bash
docker compose up -d --build   # PostgreSQL + API
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

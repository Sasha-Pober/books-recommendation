# Books Recommendation

A book store with a neural-network-based recommendation system.

| Part | Tech | Status |
|---|---|---|
| `backend/` | C# / ASP.NET Core 10, EF Core, PostgreSQL | ✅ basic API |
| `frontend/` | TypeScript / Angular | planned |
| `ml/` | Python / PyTorch recommendation service | planned |

## Backend architecture

Clean Architecture; dependencies point inward (`Api → Infrastructure → Application → Domain`).

```
backend/
├── src/
│   ├── BookStore.Domain/          entities (Book, Author, Genre, User, Rating, Interaction), no dependencies
│   ├── BookStore.Application/     use-case services, DTOs, abstractions (IApplicationDbContext, IRecommendationEngine)
│   ├── BookStore.Infrastructure/  EF Core + PostgreSQL, migrations, seeding; later: ML-service client, message broker
│   └── BookStore.Api/             controllers, error handling (ProblemDetails), OpenAPI, health checks
└── tests/
    └── BookStore.UnitTests/
```

### Recommendations

`IRecommendationEngine` returns ranked book ids. `RecommendationService` sits in front of it and:

- removes books the user already bought or rated, and books that are out of stock;
- enriches the ids with book data;
- falls back to `BaselineRecommendationEngine` (popularity plus author/genre similarity) if the primary engine fails or returns nothing;
- returns a `recommendationId` that the frontend sends back with interaction events, so clicks can be attributed to a model.

For now the baseline engine is the primary engine. The Python neural-network service will plug in as another `IRecommendationEngine` implementation.

Implicit feedback (`View`, `Click`, `AddToWishlist`, `AddToCart`, `Purchase`) is collected via `POST /api/interactions` and stored in the `interactions` table. Explicit ratings go through `PUT /api/books/{id}/rating`. These tables are the training data for the model.

## Running locally

Requirements: .NET 10 SDK, Docker.

```bash
docker compose up -d postgres          # start PostgreSQL
cd backend
dotnet run --project src/BookStore.Api # applies migrations and seeds a demo catalog in Development
```

- API reference (Scalar): http://localhost:5292/scalar
- OpenAPI document: http://localhost:5292/openapi/v1.json
- Health: http://localhost:5292/health
- Sample requests: `backend/src/BookStore.Api/BookStore.Api.http`

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

# Build context: ./backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, in its own layer, so it is cached until a project file changes.
COPY src/BookStore.Domain/BookStore.Domain.csproj src/BookStore.Domain/
COPY src/BookStore.Application/BookStore.Application.csproj src/BookStore.Application/
COPY src/BookStore.Infrastructure/BookStore.Infrastructure.csproj src/BookStore.Infrastructure/
COPY src/BookStore.Api/BookStore.Api.csproj src/BookStore.Api/
RUN dotnet restore src/BookStore.Api/BookStore.Api.csproj

COPY src/ src/
RUN dotnet publish src/BookStore.Api/BookStore.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "BookStore.Api.dll"]

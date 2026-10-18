# ProductService — DDD, CQRS & MediatR Microservice

`ProductService` is Service #1 of the .NET 10 E-Commerce Microservices learning project. It owns the **Catalog** bounded context (`productdb` in PostgreSQL 17) and is built with **Domain-Driven Design (DDD)**, **CQRS**, and **MediatR (12.5.0)**.

## Architecture & Project Structure

```text
services/ProductService/
├── ProductService.slnx
├── Directory.Build.props
├── Dockerfile
├── docs/
│   ├── design.md
│   └── openapi.yaml
├── src/
│   ├── ProductService.Domain/          # AggregateRoot (Product), Value Objects (Sku, Price), Domain Events
│   ├── ProductService.Application/     # CQRS Commands, Queries, Handlers, Validators, Pipeline Behaviors
│   ├── ProductService.Infrastructure/  # ProductDbContext (IUnitOfWork), EF Core config, Repository, ReadService
│   └── ProductService.Api/             # Thin Minimal API endpoints (ISender), ProblemDetails, Health Checks
└── tests/
    ├── ProductService.UnitTests/       # Domain, Application & NetArchTest Architecture tests
    └── ProductService.IntegrationTests/# End-to-end API + DB tests (WebApplicationFactory + Testcontainers)
```

## Prerequisites
- **.NET 10 SDK** (`10.0.x`)
- **Docker Desktop** (for PostgreSQL 17, Docker Compose, and Testcontainers)

## 1. Run Locally (with `dotnet run`)

1. Start PostgreSQL 17 from the repository root (`D:\MicroServess`):
   ```powershell
   docker compose up -d postgres
   ```
2. Run `ProductService.Api`:
   ```powershell
   dotnet run --project services/ProductService/src/ProductService.Api
   ```
   In `Development`, the service automatically applies EF Core migrations and seeds 20 sample products using `Product.Create` and `IUnitOfWork`.
3. Open Scalar UI or OpenAPI spec:
   - **Scalar UI**: `http://localhost:5001/scalar/v1`
   - **OpenAPI JSON**: `http://localhost:5001/openapi/v1.json`

## 2. Run Everything in Docker Compose

From the repository root (`D:\MicroServess`):
```powershell
docker compose up -d --build
```
Verify health:
```powershell
Invoke-WebRequest http://localhost:5001/health/live
Invoke-WebRequest http://localhost:5001/health/ready
```

## 3. API Endpoints

| Method | Endpoint | CQRS Message | Status Codes |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/products` | `CreateProductCommand` | `201`, `400`, `409`, `422` |
| `GET` | `/api/products` | `ListProductsQuery` | `200`, `400` |
| `GET` | `/api/products/{id}` | `GetProductByIdQuery` | `200`, `404` |
| `PUT` | `/api/products/{id}` | `UpdateProductCommand` | `200`, `400`, `404`, `409` (concurrency), `422` (inactive) |
| `DELETE` | `/api/products/{id}` | `DeactivateProductCommand` | `204`, `404` |
| `POST` | `/api/products/{id}/activate` | `ActivateProductCommand` | `204`, `404` |
| `GET` | `/health/live` | — | `200` |
| `GET` | `/health/ready` | — | `200`, `503` |

You can test all endpoints and error cases using [`src/ProductService.Api/ProductService.Api.http`](src/ProductService.Api/ProductService.Api.http).

## 4. Configuration

| Key / Env Var | Default (Local Dev) | Docker Compose |
| :--- | :--- | :--- |
| `ConnectionStrings:ProductDb` (`ConnectionStrings__ProductDb`) | `Host=localhost;Port=5432;Database=productdb;Username=postgres;Password=postgres` | `Host=postgres;Port=5432;Database=productdb;Username=postgres;Password=postgres` |
| `Database:SeedSampleData` | `true` | `true` |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Development` |

## 5. Run Tests (Unit, Architecture & Integration)

```powershell
dotnet test services/ProductService/ProductService.slnx
```

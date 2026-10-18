# UserService (Service #2 — Identity Bounded Context)

`UserService` is the second microservice in the .NET 10 E-Commerce Microservices learning project. It owns user registration, authentication, RS256 JWT access tokens, rotating refresh tokens with reuse detection, account lockout, password hashing, and role-based user administration.

- **Design Document**: [`docs/design.md`](docs/design.md)
- **OpenAPI Specification**: [`docs/openapi.yaml`](docs/openapi.yaml)
- **Manual HTTP Requests**: [`src/UserService.Api/UserService.Api.http`](src/UserService.Api/UserService.Api.http)

---

## Architecture Highlights

- **DDD Aggregate Root + Child Entity**:
  - `User` is the Aggregate Root and `RefreshToken` is an internal Child Entity owned exclusively by `User`.
  - Value Objects: `Email` (normalized, lowercase, validated) and `PasswordHash` (redacted in `ToString()`, never exposes raw passwords to the domain).
  - Domain outcome `RefreshOutcome` (`Rotated`, `Invalid`, `Expired`, `ReuseDetected`) allows `RefreshAccessTokenCommandHandler` to commit the revocation of all user tokens **before** throwing `UnauthorizedException` (401) when token theft/replay is detected.
- **Security**:
  - **Password Hashing**: `PBKDF2-HMAC-SHA512` via `PasswordHasher<object>` with `220,000` iterations (`>= 210,000` OWASP recommendation).
  - **JWT & JWKS**: Asymmetric `RS256` signing (`iss: "ecommerce.userservice"`, `aud: "ecommerce.api"`, 15-minute expiry) with public key discovery at `GET /.well-known/jwks.json`.
  - **Refresh Token Rotation**: 32-byte cryptographically random token (`Base64Url`), stored only as a `SHA-256` hex hash (`64` chars), 7-day expiry, maximum 5 active tokens per user, automatic pruning after 7 days.
  - **Account Lockout**: 5 consecutive failed login attempts lock the account for 15 minutes (`UserLockedOutDomainEvent`).
  - **Rate Limiting**: Fixed-window limiter (`10` requests / minute / IP) on `/api/auth/*` returning `429 Too Many Requests` with `Retry-After` and RFC 7807 `ProblemDetails`.
  - **Authorization Policies**: `"Authenticated"` and `"AdminOnly"` (`RequireRole("Admin")`).

---

## Running Locally

### 1. Start PostgreSQL & Services via Docker Compose

```powershell
docker compose up -d --build
```

- `UserService` API: `http://localhost:5002`
- Scalar API Reference (Development): `http://localhost:5002/scalar/v1`
- JWKS Endpoint: `http://localhost:5002/.well-known/jwks.json`
- Health Checks: `http://localhost:5002/health/live` and `http://localhost:5002/health/ready`
- Default Development Admin: `admin@ecommerce.local` / `Admin@123456!`

### 2. Build & Run Tests

```powershell
cd services/UserService
dotnet build UserService.slnx
dotnet test UserService.slnx
```

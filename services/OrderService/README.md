# OrderService (Service #3 — Ordering Bounded Context)

`OrderService` is the third microservice in the .NET 10 E-Commerce Microservices learning project. It manages the order lifecycle (placement, stock reservation, payment confirmation, cancellation, and expiration), maintains two separate databases for CQRS, coordinates choreography sagas with RabbitMQ, projects eventual consistency read models, and calls `ProductService` using Polly v8 resilience pipelines.

- **Design Document**: [`docs/design.md`](docs/design.md)
- **OpenAPI Specification**: [`docs/openapi.yaml`](docs/openapi.yaml)
- **Chaos Experiments**: [`docs/chaos-experiments.md`](docs/chaos-experiments.md)
- **Event Contracts**: [`../../docs/contracts/order-events.md`](../../docs/contracts/order-events.md)
- **Manual HTTP Requests**: [`src/OrderService.Api/OrderService.Api.http`](src/OrderService.Api/OrderService.Api.http)

---

## Architecture Highlights

- **DDD Aggregate Root + Child Entity**:
  - `Order` is the Aggregate Root and `OrderLine` is an internal Child Entity owned exclusively by `Order`.
  - Value Objects: `Money` (ISO currency + decimal amount), `Quantity` (positive int), and `Address` (street, city, state, postal code, country).
  - Lifecycle: `Pending` -> `StockReserved` -> `Confirmed` (or `Rejected` / `Cancelled`).
  - Optimistic concurrency: `Version` integer incremented on every aggregate transition.
- **CQRS with Two Databases (Eventual Consistency)**:
  - **Write DB (`orderdb`)**: Normalized `orders`, `order_lines`, `outbox_messages`, `inbox_messages`, and `idempotency_keys`.
  - **Read DB (`orderreaddb`)**: Denormalized `order_summaries` and `order_details` tables populated asynchronously by `OrderReadModelProjector`.
  - Projections enforce `event.Version > current.Version` to guarantee out-of-order event protection and idempotency.
- **RabbitMQ Messaging (Transactional Outbox & Inbox)**:
  - Topic exchange `ecommerce.events` and dead-letter exchange `ecommerce.events.dlx`.
  - `OutboxPublisherService`: Polls `outbox_messages` with `FOR UPDATE SKIP LOCKED`, confirms publishes via RabbitMQ async API (`IChannel`), and marks them published in the same transaction.
  - Three background consumers (`InventoryEventsConsumer`, `PaymentEventsConsumer`, `OrderReadModelConsumer`) with exponential retry backoff and routing to DLQ after 3 failures.
  - Idempotent consumer pipeline via `InboxBehavior` and `inbox_messages` table.
- **Resilience & Fault Tolerance**:
  - Outbound HTTP to `ProductService` wrapped with `Microsoft.Extensions.Http.Resilience` (Polly v8):
    1. **Total Timeout**: `8s` max across all attempts.
    2. **Retry**: `2` retries with exponential backoff and jitter (500ms initial).
    3. **Circuit Breaker**: `50%` failure rate over minimum `6` requests trips circuit open for `30s` (sampling window `20s`).
    4. **Attempt Timeout**: `2s` per individual HTTP request.
    5. `404 Not Found` responses are fast-failed without retries or circuit penalties.
- **Authentication & Security**:
  - Validates `RS256` Bearer JWTs issued by `UserService`.
  - Public keys retrieved from `UserService` JWKS endpoint (`/.well-known/jwks.json`) with a 10-minute refresh background timer and a 24-hour stale cache fallback.
- **Choreographed Saga & FulfillmentSimulator**:
  - `FulfillmentSimulator` (`tools/FulfillmentSimulator/`) simulates the asynchronous actions of upcoming `InventoryService` and `PaymentService`.

---

## Running Locally

### 1. Start Infrastructure & All Services via Docker Compose

```powershell
# Start PostgreSQL, RabbitMQ, ProductService, UserService, OrderService, and FulfillmentSimulator
docker compose --profile simulator up -d --build
```

- `OrderService` API: `http://localhost:5003`
- Scalar API Reference: `http://localhost:5003/scalar/v1`
- Health Checks:
  - Live: `http://localhost:5003/health/live`
  - Ready: `http://localhost:5003/health/ready` (checks `orderdb`, `orderreaddb`, and outbox lag)
- RabbitMQ Management Dashboard: `http://localhost:15672` (guest / guest)
- PostgreSQL: `localhost:5432` (`orderdb` and `orderreaddb` databases)

### 2. Run Tests

```powershell
cd services/OrderService
dotnet build OrderService.slnx
dotnet test OrderService.slnx
```

---

## Sample HTTP Requests

### Place an Order

```bash
curl -X POST http://localhost:5003/api/orders \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <USER_JWT>" \
  -H "X-Idempotency-Key: 7b36f014-411a-4293-85e7-22d26d833f99" \
  -d '{
    "items": [
      { "productId": "b5a93e36-7c98-4448-9c17-43a9ad080512", "quantity": 2 }
    ],
    "shippingAddress": {
      "street": "123 Main St",
      "city": "Seattle",
      "state": "WA",
      "postalCode": "98101",
      "country": "USA"
    }
  }'
```

### Get Order Details by ID (Read DB)

```bash
curl -X GET http://localhost:5003/api/orders/e20b3307-e817-4008-8e6c-0e69be6d5b0a \
  -H "Authorization: Bearer <USER_JWT>"
```

### Get Customer Orders (Read DB)

```bash
curl -X GET "http://localhost:5003/api/orders?page=1&pageSize=10" \
  -H "Authorization: Bearer <USER_JWT>"
```

### Cancel Order

```bash
curl -X POST http://localhost:5003/api/orders/e20b3307-e817-4008-8e6c-0e69be6d5b0a/cancel \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <USER_JWT>" \
  -d '{ "reason": "Customer requested cancellation before shipment." }'
```

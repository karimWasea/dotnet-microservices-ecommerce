# OrderService Chaos & Resilience Experiments

This document details 4 real-world failure scenarios to verify that `OrderService` handles distributed system faults gracefully according to microservices best practices.

---

## Experiment 1: ProductService is Down (Retry & Circuit Breaker)

### Goal
Verify that when `ProductService` is unavailable:
1. `OrderService` attempts retries with exponential backoff and jitter.
2. If failures persist, returns `503 Service Unavailable` RFC 7807 ProblemDetails.
3. After 6 consecutive failures (50% threshold within 20s window), the circuit breaker transitions to **OPEN**.
4. While open, requests fail immediately without making network calls.
5. Once `ProductService` recovers and the 30s break duration elapses, the circuit transitions to **HALF-OPEN** and then **CLOSED**.

### Step-by-Step Commands

1. **Stop ProductService container**:
   ```bash
   docker compose stop product-service
   ```

2. **Send an Order Placement request**:
   ```bash
   curl -i -X POST http://localhost:5003/api/orders \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer <VALID_JWT>" \
     -H "X-Idempotency-Key: chaos-exp1-attempt1" \
     -d '{
       "items": [{ "productId": "b5a93e36-7c98-4448-9c17-43a9ad080512", "quantity": 1 }],
       "shippingAddress": {
         "street": "100 Main", "city": "Seattle", "state": "WA", "postalCode": "98101", "country": "USA"
       }
     }'
   ```

3. **Expected Response**:
   `HTTP/1.1 503 Service Unavailable`
   ```json
   {
     "type": "https://datatracker.ietf.org/doc/html/rfc7807",
     "title": "Dependency Unavailable",
     "status": 503,
     "detail": "Product catalog service is currently unavailable.",
     "instance": "/api/orders"
   }
   ```

4. **Trip the Circuit Breaker**:
   Repeat the curl request 6 times rapidly with distinct idempotency keys.
   Inspect OrderService container logs:
   ```bash
   docker compose logs --tail 40 order-service
   ```
   **Observed Log**:
   ```text
   warn: OrderService.Infrastructure.HttpClients.ProductCatalogHttpClient
         Circuit breaker opened for 30s due to failure rate exceeding threshold.
   ```

5. **Verify Fast Failure**:
   Send a 7th request immediately. The response returns in < 5ms without network delays because the circuit is OPEN.

6. **Recover**:
   ```bash
   docker compose start product-service
   ```
   Wait 30 seconds. Send a new request: the probe call succeeds, and the circuit transitions back to CLOSED.

---

## Experiment 2: RabbitMQ is Down (Transactional Outbox Resilience)

### Goal
Verify that `OrderService` avoids the "Dual Write" trap: orders are committed to PostgreSQL write database even if RabbitMQ is completely offline.

### Step-by-Step Commands

1. **Stop RabbitMQ container**:
   ```bash
   docker compose stop rabbitmq
   ```

2. **Place an order**:
   ```bash
   curl -i -X POST http://localhost:5003/api/orders \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer <VALID_JWT>" \
     -H "X-Idempotency-Key: chaos-exp2-order1" \
     -d '{
       "items": [{ "productId": "b5a93e36-7c98-4448-9c17-43a9ad080512", "quantity": 1 }],
       "shippingAddress": {
         "street": "200 Pine", "city": "Seattle", "state": "WA", "postalCode": "98101", "country": "USA"
       }
     }'
   ```

3. **Expected Response**:
   `HTTP/1.1 201 Created`
   The order is successfully placed and committed to `orderdb`!

4. **Verify Outbox in PostgreSQL**:
   ```bash
   docker compose exec postgres psql -U postgres -d orderdb -c \
     "SELECT id, event_type, published_at FROM outbox_messages WHERE published_at IS NULL;"
   ```
   **Output**:
   ```text
                     id                  |        event_type         | published_at 
   --------------------------------------+---------------------------+--------------
    a12f6e91-7235-433e-b816-64c8d76d6541 | OrderPlacedIntegrationEvent | 
   ```
   The event is safely persisted. `OutboxPublisherService` logs backoff retries:
   ```text
   warn: OrderService.Infrastructure.Messaging.OutboxPublisherService
         RabbitMQ connection unavailable. Retrying in 2000ms...
   ```

5. **Recover RabbitMQ**:
   ```bash
   docker compose start rabbitmq
   ```
   Within seconds, `OutboxPublisherService` reconnects, acquires the pending message with `FOR UPDATE SKIP LOCKED`, receives publisher confirmation, and stamps `published_at = CURRENT_TIMESTAMP`.

---

## Experiment 3: ProductService is Slow (Attempt Timeout & Total Budget)

### Goal
Verify that individual attempts timeout after 2 seconds, and the total pipeline budget enforces an 8-second ceiling.

### Simulation
Using an HTTP mock / latency proxy or by configuring delay in development:
1. ProductService endpoint responds with a 3-second delay.
2. Attempt timeout fires at 2,000ms and logs:
   ```text
   warn: OrderService.Infrastructure.HttpClients.ProductCatalogHttpClient
         Request attempt to ProductCatalog timed out after 2000ms. Retrying attempt 1/2...
   ```
3. Polly retries up to 2 times with jittered exponential backoff.
4. Total timeout guarantees OrderService clients never hang indefinitely.

---

## Experiment 4: Duplicate Events & Out-of-Order Delivery (Inbox & Versioning)

### Goal
Verify that if RabbitMQ delivers the same event more than once (at-least-once delivery guarantee) or out-of-order, `OrderService` avoids corrupted data.

### Step-by-Step Commands

1. **Simulate Duplicate Delivery**:
   Publish a duplicate `InventoryStockReservedIntegrationEvent` with the same `messageId` into `ecommerce.events` exchange.

2. **Inspect Consumer Logs**:
   ```bash
   docker compose logs --tail 30 order-service
   ```
   **Observed Log**:
   ```text
   info: OrderService.Application.Behaviors.InboxBehavior
         Message 3c79e7f8-9a67-4cb2-8173-61bb16d00472 already processed in inbox. Skipping.
   ```
   The duplicate event is acknowledged (`BasicAckAsync`) without duplicate processing.

3. **Read Model Version Guard**:
   If an out-of-order event arrives where `event.Version <= readModel.Version`, `OrderReadModelProjector` ignores the stale projection update, ensuring the read database reflects the latest state.

# Order Events Contract (`schema-version: 1`)

This document defines the asynchronous messaging contract between **OrderService**, **InventoryService**, **PaymentService** (currently simulated by **FulfillmentSimulator**), and the **OrderService Read-Model Projector**.

> **Contract Evolution Rule**: All payloads are UTF-8 JSON with `camelCase` property names and UTC ISO-8601 timestamps. Adding optional fields is backward-compatible; removing, renaming, or changing the type of an existing field is a breaking change and requires a new schema version.

---

## 1. RabbitMQ Topology

- **Topic Exchange**: `ecommerce.events` (`topic`, `durable: true`, `autoDelete: false`)
- **Dead-Letter Exchange (DLX)**: `ecommerce.events.dlx` (`direct`, `durable: true`, `autoDelete: false`)

| Queue Name | Bound Exchange | Routing Key Pattern(s) | Dead-Letter Queue (DLQ) | Consumer |
|---|---|---|---|---|
| `order-service.inventory` | `ecommerce.events` | `inventory.reserved`, `inventory.reservation-failed` | `order-service.inventory.dlq` | `OrderService` (`InventoryEventsConsumer`) |
| `order-service.payment` | `ecommerce.events` | `payment.succeeded`, `payment.failed` | `order-service.payment.dlq` | `OrderService` (`PaymentEventsConsumer`) |
| `order-service.read-model` | `ecommerce.events` | `order.*` | `order-service.read-model.dlq` | `OrderService` (`OrderReadModelConsumer`) |
| `simulator.orders` | `ecommerce.events` | `order.placed`, `order.cancelled` | `simulator.orders.dlq` | `FulfillmentSimulator` |

---

## 2. AMQP Message Envelope

Every published message MUST set the following AMQP 0-9-1 basic properties:

| Property / Header | Value / Format | Purpose |
|---|---|---|
| `DeliveryMode` | `2` (Persistent) | Survives broker restarts |
| `ContentType` | `application/json` | UTF-8 JSON payload |
| `MessageId` | `Guid` v7 string (e.g., `"019a0000-..."`) | Unique ID of the outbox message; used by `inbox_messages` for idempotency |
| `Type` | Routing key (e.g., `"order.placed"`) | Event type discriminator |
| `Timestamp` | Unix epoch seconds (UTC) | Message creation timestamp |
| `CorrelationId` | `X-Correlation-Id` string | End-to-end distributed tracing correlation ID |
| Header `x-schema-version` | `1` (integer) | Contract schema version |
| Header `x-source` | `"order-service"` or `"fulfillment-simulator"` | Producing service name |

---

## 3. Events Published by `OrderService`

### 3.1 `order.placed`
Published when a new order is placed (`Version = 1`, `Status = "Pending"`). Carries the full order snapshot so downstream services and the read-model projector do not need to query `orderdb`.

```json
{
  "orderId": "019a0100-1111-7000-8000-000000000001",
  "userId": "019a0050-2222-7000-8000-000000000002",
  "version": 1,
  "status": "Pending",
  "items": [
    {
      "productId": "019a0010-3333-7000-8000-000000000003",
      "productName": "Wireless Mechanical Keyboard",
      "unitPrice": 2500.00,
      "quantity": 2,
      "lineTotal": 5000.00
    }
  ],
  "shippingAddress": {
    "recipientName": "Alice Customer",
    "street": "123 Nile Corniche",
    "city": "Cairo",
    "country": "EG"
  },
  "total": 5000.00,
  "currency": "EGP",
  "placedAtUtc": "2026-10-19T08:00:00Z"
}
```

### 3.2 `order.stock-reserved`
Published when `Order` transitions to `StockReserved`.

```json
{
  "orderId": "019a0100-1111-7000-8000-000000000001",
  "userId": "019a0050-2222-7000-8000-000000000002",
  "version": 2,
  "occurredAtUtc": "2026-10-19T08:00:01Z"
}
```

### 3.3 `order.confirmed`
Published when `Order` transitions to `Confirmed` after payment succeeds.

```json
{
  "orderId": "019a0100-1111-7000-8000-000000000001",
  "userId": "019a0050-2222-7000-8000-000000000002",
  "version": 3,
  "total": 5000.00,
  "currency": "EGP",
  "paymentId": "pay_019a01009999",
  "occurredAtUtc": "2026-10-19T08:00:02Z"
}
```

### 3.4 `order.rejected`
Published when `Order` transitions from `Pending` to `Rejected` because stock reservation failed.

```json
{
  "orderId": "019a0100-1111-7000-8000-000000000001",
  "userId": "019a0050-2222-7000-8000-000000000002",
  "version": 2,
  "reason": "Insufficient stock for requested quantity.",
  "occurredAtUtc": "2026-10-19T08:00:01Z"
}
```

### 3.5 `order.cancelled`
Published when `Order` transitions from `Pending` or `StockReserved` to `Cancelled` (by `Customer`, `Admin`, or `System` on payment failure or expiration timeout). Acts as the **Saga Compensation Event** instructing `InventoryService` to release any reserved stock.

```json
{
  "orderId": "019a0100-1111-7000-8000-000000000001",
  "userId": "019a0050-2222-7000-8000-000000000002",
  "version": 3,
  "reason": "PaymentFailed",
  "cancelledBy": "System",
  "occurredAtUtc": "2026-10-19T08:00:02Z"
}
```

---

## 4. Events Consumed by `OrderService`

### 4.1 `inventory.reserved`
Published by `InventoryService` (or `FulfillmentSimulator`) when stock is reserved for an order.

```json
{
  "orderId": "019a0100-1111-7000-8000-000000000001",
  "occurredAtUtc": "2026-10-19T08:00:01Z"
}
```

### 4.2 `inventory.reservation-failed`
Published by `InventoryService` (or `FulfillmentSimulator`) when stock cannot be reserved.

```json
{
  "orderId": "019a0100-1111-7000-8000-000000000001",
  "reason": "Quantity exceeds available stock.",
  "occurredAtUtc": "2026-10-19T08:00:01Z"
}
```

### 4.3 `payment.succeeded`
Published by `PaymentService` (or `FulfillmentSimulator`) when payment is captured.

```json
{
  "orderId": "019a0100-1111-7000-8000-000000000001",
  "paymentId": "pay_019a01009999",
  "amount": 5000.00,
  "currency": "EGP",
  "occurredAtUtc": "2026-10-19T08:00:02Z"
}
```

### 4.4 `payment.failed`
Published by `PaymentService` (or `FulfillmentSimulator`) when payment fails. Triggers order cancellation and saga stock compensation.

```json
{
  "orderId": "019a0100-1111-7000-8000-000000000001",
  "reason": "Card declined: limit exceeded.",
  "occurredAtUtc": "2026-10-19T08:00:02Z"
}
```

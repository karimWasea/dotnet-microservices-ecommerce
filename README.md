# E-Commerce .NET 10 Microservices Learning Project

This repository contains the microservices for our e-commerce system, built step by step with .NET 10, Minimal APIs, EF Core, and PostgreSQL 17.

## Services
- **[ProductService](services/ProductService/README.md)** (`http://localhost:5001`) — Owns the product catalog (`productdb`).

## Quick Start (Docker Compose)
```powershell
docker compose up -d --build
```
- ProductService API: `http://localhost:5001/api/products`
- ProductService Scalar UI: `http://localhost:5001/scalar/v1`
- Health Checks: `http://localhost:5001/health/live` and `http://localhost:5001/health/ready`

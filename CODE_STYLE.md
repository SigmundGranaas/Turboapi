# Turboapi Architecture Guide

## System Architecture

Turboapi is a microservices-based application built with .NET 9, consisting of:

- **Turboapi-auth**: Authentication service (user registration, login, tokens)
- **Turboapi-geo**: Location service with geospatial capabilities
- **Turboapi-activity**: Activity management service
- **Turbo-event**: Shared event handling library
- **Turbo-pg-data**: Shared PostgreSQL operations library

### Key Architecture Patterns

- **Domain-Driven Design**: Bounded contexts per microservice
- **CQRS**: Command/Query Responsibility Segregation
- **Event Sourcing**: Domain events as source of truth
- **Microservices**: Independent deployment and scaling

### Infrastructure

- **Docker**: Containerized services
- **Kafka**: Event streaming backbone
- **PostgreSQL**: Database with extensions (PostGIS)
- **Flyway**: Database migrations
- **OpenTelemetry/Prometheus/Grafana**: Monitoring stack

## Domain Modeling

- **Aggregates**: Core domain entities (Location, Activity, User)
- **Value Objects**: Immutable objects without identity (LatLng)
- **Domain Events**: Rich event model for cross-service consistency
- **Commands**: Represent user intent to change system state
- **Queries**: Read-only operations against materialized views

## Testing Approaches

1. **Unit Tests**
   - Domain model and business logic testing
   - Command/query handler tests for interfacing with the domain
   - No mocking. Object graphs are alwways reconstructed, using only mocks for out of process dependencies

2. **Integration Tests**
   - Database integration with Testcontainers
   - Kafka integration for event handling
   - API endpoint testing

3. **Performance Tests (k6)**
   - Load testing scripts in `/performance/k6`
   - Stress testing for limits

## Coding Practices

1. **Clean Architecture**
   - Domain core isolated from infrastructure
   - Dependency inversion with interfaces

2. **SOLID Principles**
   - Single Responsibility: Focused classes
   - Interface Segregation: Focused interfaces
   - Dependency Inversion: Dependency injection

3. **Error Handling**
   - Domain-specific exceptions
   - Global exception middleware

4. **Observability**
   - Structured logging
   - Distributed tracing
   - Metrics collection

5. **Security**
   - JWT authentication. (User id is embedded into the key)
   - Authorization middleware
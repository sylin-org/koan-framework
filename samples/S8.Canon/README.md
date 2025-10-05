# S8.Canon - Customer Canonization Sample

Demonstrates **Canon Runtime** pipeline-based canonization using entity-first patterns.

## Overview

This sample shows how raw customer data flows through a canonization pipeline to produce validated, enriched canonical entities:

```
Raw Customer → Validation → Enrichment → Canonical Storage
```

### Key Patterns Demonstrated

1. **CanonEntity<T>** - Entity-first canonical entities with auto GUID v7 IDs
2. **ICanonPipelineContributor** - Pipeline phase contributors for validation and enrichment
3. **ICanonRuntime** - Runtime-driven canonization execution
4. **CanonEntitiesController<T>** - Auto-generated canonization API endpoints
5. **Auto-Registration** - KoanAutoRegistrar for zero-config module loading

## Architecture

### Customer Entity

`Domain/Customer.cs` - Canonical customer entity:
- Inherits from `CanonEntity<Customer>`
- Auto GUID v7 ID generation
- Properties: Email, Phone, FirstName, LastName, DisplayName, AccountTier, Country, Language
- Entity-first patterns: `Customer.Get()`, `customer.Save()`

### Canonization Pipeline

**Phase 1: Validation** (`Pipeline/CustomerValidationContributor.cs`)
- Validates required fields (email, first name, last name)
- Validates email format (regex)
- Validates phone format (E.164)
- Normalizes data (lowercase email, trim whitespace, standardize phone)
- Returns error event if validation fails

**Phase 2: Enrichment** (`Pipeline/CustomerEnrichmentContributor.cs`)
- Computes `DisplayName` from FirstName + LastName (fallback to email prefix)
- Computes `AccountTier` based on business rules:
  - **Premium**: Customers from premium countries (US, GB, DE, FR, JP, AU, CA) with complete profiles
  - **Standard**: Customers with phone number
  - **Basic**: Everyone else
- Sets customer state to Canonical/Ready
- Adds enrichment metadata tags

### Pipeline Registration

`Pipeline/CustomerPipelineRegistrar.cs` - Implements `ICanonRuntimeConfigurator`:
- Registers Customer pipeline with validation + enrichment contributors
- Auto-discovered and registered via `KoanAutoRegistrar`

### API Layer

`Controllers/CustomersController.cs` - Inherits from `CanonEntitiesController<Customer>`:
- Auto-generated CRUD endpoints
- Auto-generated canonization endpoint
- Custom endpoint: `GET /api/canon/customers/by-tier/{tier}`

## Quick Start

### 1. Run the Application

```bash
dotnet run --project samples/S8.Canon
```

The API will start at `http://localhost:5000` (or `https://localhost:5001`).

### 2. Explore Swagger UI

Navigate to `http://localhost:5000/swagger` to explore the API.

### 3. Canonize a Customer

**Valid Customer (Premium Tier):**

```bash
curl -X POST http://localhost:5000/api/canon/customers \
  -H "Content-Type: application/json" \
  -d '{
    "email": "alice@example.com",
    "phone": "+1-202-555-0101",
    "firstName": "Alice",
    "lastName": "Smith",
    "country": "US",
    "language": "en"
  }'
```

**Expected Result:**
```json
{
  "entity": {
    "id": "01JXXXXXXXXXXXXXXXXXXXXXX",
    "email": "alice@example.com",
    "phone": "+12025550101",
    "firstName": "Alice",
    "lastName": "Smith",
    "displayName": "Alice Smith",
    "accountTier": "Premium",
    "country": "US",
    "language": "en",
    "state": {
      "lifecycle": "Canonical",
      "readiness": "Ready"
    }
  },
  "outcome": "Canonized",
  "events": [
    {
      "phase": "Validation",
      "message": "Completed Validation phase."
    },
    {
      "phase": "Aggregation",
      "message": "Completed Aggregation phase."
    }
  ]
}
```

**Valid Customer (Standard Tier):**

```bash
curl -X POST http://localhost:5000/api/canon/customers \
  -H "Content-Type: application/json" \
  -d '{
    "email": "bob@example.com",
    "phone": "+44-20-7946-0958",
    "firstName": "Bob",
    "lastName": "Jones"
  }'
```

**Expected Tier:** `Standard` (has phone but not from premium country)

**Valid Customer (Basic Tier):**

```bash
curl -X POST http://localhost:5000/api/canon/customers \
  -H "Content-Type: application/json" \
  -d '{
    "email": "charlie@example.com",
    "firstName": "Charlie",
    "lastName": "Brown"
  }'
```

**Expected Tier:** `Basic` (no phone number)

### 4. Test Validation Errors

**Missing Required Fields:**

```bash
curl -X POST http://localhost:5000/api/canon/customers \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test@example.com"
  }'
```

**Expected Error:**
```json
{
  "outcome": "Failed",
  "events": [
    {
      "phase": "Validation",
      "message": "Customer validation failed",
      "detail": "FirstName is required; LastName is required"
    }
  ]
}
```

**Invalid Email Format:**

```bash
curl -X POST http://localhost:5000/api/canon/customers \
  -H "Content-Type: application/json" \
  -d '{
    "email": "not-an-email",
    "firstName": "Test",
    "lastName": "User"
  }'
```

**Expected Error:**
```json
{
  "outcome": "Failed",
  "events": [
    {
      "phase": "Validation",
      "message": "Customer validation failed",
      "detail": "Invalid email format: not-an-email"
    }
  ]
}
```

### 5. Query Customers

**Get All Customers:**

```bash
curl http://localhost:5000/api/canon/customers
```

**Get Customer by ID:**

```bash
curl http://localhost:5000/api/canon/customers/{id}
```

**Get Customers by Tier:**

```bash
curl http://localhost:5000/api/canon/customers/by-tier/Premium
curl http://localhost:5000/api/canon/customers/by-tier/Standard
curl http://localhost:5000/api/canon/customers/by-tier/Basic
```

### 6. Advanced Features

**Rebuild Customer Views (Force Reprojection):**

```bash
curl -X POST http://localhost:5000/api/canon/customers/{id}/rebuild
```

**Canonize with Custom Options (Headers):**

```bash
curl -X POST http://localhost:5000/api/canon/customers \
  -H "Content-Type: application/json" \
  -H "X-Canon-Origin: web-signup" \
  -H "X-Correlation-ID: 12345" \
  -H "X-Canon-Tag-source: registration-form" \
  -d '{...}'
```

## Configuration

### Data Storage

Edit `appsettings.json` to change storage provider:

```json
{
  "Koan": {
    "Data": {
      "Provider": "json",  // or "mongodb", "postgresql", etc.
      "Json": {
        "Path": "./data"
      }
    }
  }
}
```

### Canon Runtime Options

Configure default canonization options:

```csharp
// In Program.cs or startup configuration
builder.Services.AddCanonRuntime(runtime =>
{
    runtime.DefaultOptions.StageBehavior = CanonStageBehavior.Immediate;
    runtime.DefaultOptions.SkipDistribution = false;
    runtime.RecordCapacity = 10000; // Replay buffer size
});
```

## Testing

The sample includes comprehensive test scenarios:

```bash
# Run all tests
dotnet test

# Run specific test
dotnet test --filter "FullyQualifiedName~CustomerCanonicalizationTests"
```

## Key Concepts

### Entity-First Pattern

```csharp
// ✅ Correct: Entity-first pattern
var customer = new Customer
{
    Email = "alice@example.com",
    FirstName = "Alice",
    LastName = "Smith"
};

// Save (canonize + persist)
await customer.Save();

// Get (retrieve by ID)
var loaded = await Customer.Get(customer.Id);

// Query (all entities)
var all = await Customer.All();
```

### Pipeline Contributors

```csharp
public class MyContributor : ICanonPipelineContributor<Customer>
{
    public CanonPipelinePhase Phase => CanonPipelinePhase.Validation;

    public async Task<CanonizationEvent?> ExecuteAsync(
        CanonPipelineContext<Customer> context,
        CancellationToken cancellationToken)
    {
        // Validate/transform the entity
        if (IsInvalid(context.Entity))
        {
            return new CanonizationEvent
            {
                Phase = Phase,
                Message = "Validation failed",
                Detail = "..."
            };
        }

        // Return null for success (default event generated)
        return null;
    }
}
```

### Auto-Registration

```csharp
// Just reference the assembly - pipeline auto-registers
// No manual service.AddXyz() calls needed!

// KoanAutoRegistrar discovers and registers:
// - ICanonRuntimeConfigurator implementations
// - CanonEntity<T> types
// - Pipeline contributors
```

## Next Steps

1. **Add More Phases** - Implement contributors for Policy, Projection, Distribution phases
2. **Add Observers** - Register `ICanonPipelineObserver` for telemetry and auditing
3. **Multi-Provider** - Test with MongoDB, PostgreSQL, etc. (same code, different storage)
4. **Custom Controllers** - Extend `CanonEntitiesController<T>` with business-specific endpoints
5. **Integration Tests** - Add end-to-end tests for complex canonization scenarios

## References

- [Canon Runtime Specification](../../docs/specifications/SPEC-canon-runtime.md)
- [Canon Runtime Architecture ADR](../../docs/decisions/ARCH-0058-canon-runtime-architecture.md)
- [Entity Capabilities How-To](../../docs/guides/entity-capabilities-howto.md)
- [Canon Runtime Migration Plan](../../docs/architecture/canon-runtime-migration.md)

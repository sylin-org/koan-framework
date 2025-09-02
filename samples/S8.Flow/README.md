# S8.Flow Sample

This sample demonstrates Sora Flow capabilities for data ingestion, association, and projection across multiple data sources.

## Scenarios

### 1. Customer Journey Flow

Demonstrates how data from different touchpoints gets associated to a single customer profile:

- **Web Registration**: Customer signs up via website (email + phone)
- **Social Media**: Customer interacts on social platforms (handle + email)
- **IoT Device**: Customer's smart device sends telemetry (phone as owner ID)

All three data points should associate to the same reference via shared email/phone keys.

### 2. Multi-Owner Collision

Shows how Flow rejects data when aggregation keys belong to different existing references:

- Creates two separate customer profiles with different emails
- Attempts to ingest data containing both emails
- Results in `MULTI_OWNER_COLLISION` rejection

### 3. No-Keys Rejection

Demonstrates rejection when data lacks configured aggregation keys:

- Ingests anonymous session data without email/phone/handle
- Results in `NO_KEYS` rejection

## API Endpoints

### Ingestion

- `POST /api/ingestion/customer` - Ingest customer registration data
- `POST /api/ingestion/social` - Ingest social media interactions
- `POST /api/ingestion/iot` - Ingest IoT device events
- `POST /api/ingestion/batch` - Batch ingestion endpoint

### Analytics

- `GET /api/analytics/customer-360/{referenceUlid}` - Get unified customer view
- `GET /api/analytics/pipeline-stats` - Flow pipeline health metrics
- `GET /api/analytics/rejections` - Rejection analysis for debugging

### Sample Data

- `POST /api/sampledata/customer-journey` - Generate complete customer journey
- `POST /api/sampledata/collision-scenario` - Generate collision test data
- `POST /api/sampledata/no-keys-scenario` - Generate rejection test data

### Flow Views (from Sora.Flow.Web)

- `GET /flow/views/canonical` - Canonical projections (unique values per tag)
- `GET /flow/views/lineage` - Lineage projections (sources per value)

## Quick Start

1. **Start the application**:

   ```bash
   dotnet run
   ```

2. **Generate sample customer journey**:

   ```bash
   curl -X POST http://localhost:5000/api/sampledata/customer-journey
   ```

3. **Wait for association** (background workers run every ~2-5 seconds)

4. **Check pipeline stats**:

   ```bash
   curl http://localhost:5000/api/analytics/pipeline-stats
   ```

5. **View customer 360** (use ULID from `ReferenceItem.Id`, aka ReferenceUlid):
   ```bash
   curl http://localhost:5000/api/analytics/customer-360/{referenceUlid}
   ```

## Configuration

Flow is configured with ubiquitous aggregation tags:

- `email` - Email addresses
- `phone` - Phone numbers
- `handle` - Social media handles

See `Program.cs` for FlowOptions configuration.

## Data Flow

1. **Ingest**: Records enter via API → stored in `intake` set
2. **Associate**: Background worker processes intake → derives reference IDs → moves to `keyed` set
3. **Project**: Background worker reads keyed records → builds canonical/lineage views → stores in view sets

Rejections are stored in `rejections` with reason codes for debugging.

## Architecture

- Uses JSON adapter for simplicity (file-based storage)
- Background hosted services handle association and projection
- TTL purge enabled (30min intervals)
- Swagger UI available at `/swagger`

## Sample Aggregation Keys

All scenarios use shared constants from `Sora.Testing.Flow.FlowTestConstants`:

- Email: `a@example.com`, `b@example.com`
- Phone: `+1-202-555-0101`, `+1-202-555-0102`
- Handle: `@alice`, `@bob`

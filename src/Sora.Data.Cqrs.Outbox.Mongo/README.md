# Sora.Data.Cqrs.Outbox.Mongo

MongoDB-backed IOutboxStore for Sora's implicit CQRS pipeline.

- Durable outbox storage using MongoDB
- Leased dequeuing to avoid concurrent delivery
- Unique index on DedupKey (if provided in the future)

Quick start
- Just reference this package and the provider will be discovered automatically. The outbox selector picks the highest ProviderPriority and prefers Mongo over the in-memory default.
- Optionally, call services.AddMongoOutbox() to register explicitly or to override options in code.

## Install and enable

1) Configure from appsettings (bound from `Sora:Cqrs:Outbox:Mongo`). Connection will resolve in this order:
- options.ConnectionString (explicit)
- Sora:Data:Sources:{name}:mongo:ConnectionString (named source; {name} = ConnectionStringName, default "mongo")
- ConnectionStrings:{name}

Example appsettings.json (named connection via ConnectionStrings)

```json
{
  "Sora": {
    "Cqrs": {
      "Outbox": {
        "Mongo": {
          // ConnectionStringName defaults to "mongo"; set explicitly if you like
          "ConnectionStringName": "mongo",
          "Database": "sora",
          "Collection": "Outbox",
          "LeaseSeconds": 30,
          "MaxAttempts": 10
        }
      }
    }
  },
  "ConnectionStrings": {
    "mongo": "mongodb://localhost:27017"
  }
}
```

Alternative (inline connection string under Sora:Cqrs:Outbox:Mongo)

```json
{
  "Sora": {
    "Cqrs": {
      "Outbox": {
        "Mongo": {
          "ConnectionString": "mongodb://localhost:27017",
          "Database": "sora",
          "Collection": "Outbox"
        }
      }
    }
  }
}
```

2) Register the store in your service setup:

services.AddMongoOutbox(); // binds from Sora:Cqrs:Outbox:Mongo and defaults name to "mongo"

Or override via options:

services.AddMongoOutbox(o =>
{
    o.ConnectionStringName = "custom-mongo"; // or set o.ConnectionString directly
    o.Database = "sora";
    o.Collection = "Outbox";
    o.LeaseSeconds = 30;
    o.MaxAttempts = 10;
});

Notes:
- Cqrs initializer registers a default in-memory outbox. Adding Mongo will override it (highest ProviderPriority wins; explicit registration also works).
- The outbox processor (in Sora.Data.Cqrs) will pick up this store automatically.

## Schema and indexes

The store creates indexes on:
- Status + VisibleAt (for efficient pending scans)
- LeaseUntil (to reclaim expired leases)
- DedupKey (unique, sparse)

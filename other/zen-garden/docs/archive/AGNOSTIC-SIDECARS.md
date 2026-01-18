# Agnostic Sidecars (Future)

**Optional services that handle category-based discovery requests.**

---

## Concept

Some offerings can provide **two discovery endpoints**:

1. **Native service** - Specific implementation (e.g., `zen-garden:mongodb`)
2. **Agnostic sidecar** - Category-based handler (e.g., `zen-garden:database`, `zen-garden:document-database`)

---

## Use Cases

### **Applications with Native Drivers**
```javascript
// App uses MongoDB-specific features
const uri = await resolve('zen-garden:mongodb');
// → mongodb://stone-01:27017

// Full MongoDB API available
const client = new MongoClient(uri);
// Uses change streams, transactions, aggregation pipeline, etc.
```

### **Applications Without Native Drivers**
```javascript
// App uses generic database operations
const uri = await resolve('zen-garden:database');
// → http://stone-01:8080 (agnostic sidecar)

// Standardized HTTP API
const users = await fetch(`${uri}/myapp/entities/users`).then(r => r.json());
// Works identically whether backend is MongoDB, PostgreSQL, SQL Server, etc.
```

---

## Discovery Resolution

**Specific requests** (native):
```
zen-garden:mongodb        → MongoDB native protocol (port 27017)
zen-garden:postgresql     → PostgreSQL native protocol (port 5432)
zen-garden:redis          → Redis native protocol (port 6379)
```

**Category requests** (agnostic):
```
zen-garden:database               → Agnostic data API (port 8080)
zen-garden:document-database      → Agnostic data API (port 8080)
zen-garden:relational-database    → Agnostic data API (port 8080)
```

Both announcements come from the **same Stone**, referencing the **same backend service**.

---

## mDNS Dual Announcements

**Each database service has its own sidecar.** A Stone running multiple databases will have multiple sidecars (one per service).

**Example Stone running MongoDB:**

```
# Native MongoDB announcement
offering=mongodb
port=27017
protocol=native
version=7.0.4
categories=database,document-database

# MongoDB's agnostic sidecar announcement
offering=mongodb-agnostic
port=8080
protocol=agnostic
version=7.0.4
categories=database,document-database
```

**Example Stone running MongoDB AND SQL Server:**

```
# Native MongoDB announcement
offering=mongodb
port=27017
protocol=native
version=7.0.4
categories=database,document-database

# MongoDB's sidecar
offering=mongodb-agnostic
port=8080
protocol=agnostic
version=7.0.4
categories=database,document-database

# Native SQL Server announcement
offering=sqlserver
port=1433
protocol=native
version=2022
categories=database,relational-database

# SQL Server's sidecar
offering=sqlserver-agnostic
port=8081
protocol=agnostic
version=2022
categories=database,relational-database
```

Applications choose which to use based on their requirements. Sidecars are **per-service**, not shared.

---

## When to Use Each

**Use native service when:**
- Application has vendor-specific driver installed
- Need full feature set (MongoDB change streams, PostgreSQL LISTEN/NOTIFY, etc.)
- Performance critical (no HTTP overhead)
- Vendor-specific APIs required

**Use agnostic sidecar when:**
- Application is database-agnostic
- Don't want to bundle native drivers
- Prefer HTTP/REST over wire protocols
- Need backend portability (switch databases without code changes)

---

## Manifest Structure (Proposed)

```yaml
# manifests/mongodb.yml
name: mongodb
offering: mongodb
categories:
  - database
  - document-database

docker:
  services:
    # Native service
    mongodb:
      image: mongo:7
      ports: ["27017:27017"]
      volumes: ["mongo-data:/data/db"]
    
    # Dedicated sidecar for THIS MongoDB instance
    mongodb-data-api:
      image: zen-garden/data-api:latest
      ports: ["8080:8080"]
      environment:
        CONNECTION_STRING: mongodb://mongodb:27017
      depends_on: [mongodb]

announcements:
  # Native announcement
  - offering: mongodb
    port: 27017
    protocol: native
    categories: [database, document-database]
    
  # Sidecar announcement (dedicated to this MongoDB)
  - offering: mongodb-agnostic
    port: 8080
    protocol: agnostic
    categories: [database, document-database]
```

---

## Implementation Status

**Current:** Not implemented. Stones only announce native services.

**Future exploration needed:**
- Standardized agnostic API design
- Sidecar implementation (language, framework)
- Backend adapter architecture
- Set-based namespace isolation
- Query/filter capabilities
- Error handling and translation

---

## Design Principles (When Implemented)

1. **Optional, not required** - Stones can run without sidecars
2. **Backend-agnostic** - Same API works with any backend
3. **Discoverable** - Clients can query capabilities at runtime
4. **Extensible** - Reserved namespaces for future features
5. **Koan-compatible** - Reuse existing patterns where possible

---

## Related

- [Understanding](UNDERSTANDING.md) - Core Zen Garden concepts
- [Technical Reference](REFERENCE.md) - mDNS protocol details
- [Service Manifests](../manifests/) - Current service definitions

---

**Status:** Design exploration. Not currently implemented.

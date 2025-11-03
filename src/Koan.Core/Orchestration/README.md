# Orchestration Utilities

This directory contains **service discovery** and **orchestration utilities** for Koan Framework.

---

## 🔧 Available Utilities

### ConnectionStringParser (Static Helper)

**File**: `ConnectionStringParser.cs`
**Pattern**: Static utility class for pure parsing functions
**When to Use**: Any scenario requiring database connection string manipulation

#### Supported Providers

- PostgreSQL (Npgsql)
- SQL Server (Microsoft.Data.SqlClient)
- MongoDB
- Redis
- SQLite

#### Quick Example

```csharp
using Koan.Core.Orchestration;

// Build a connection string
var connStr = ConnectionStringParser.BuildPostgresConnectionString(
    host: "localhost",
    port: 5432,
    database: "mydb",
    username: "admin",
    password: "secret"
);

// Parse a connection string
var (host, port, db, user, pwd) = ConnectionStringParser.ParsePostgresConnectionString(connStr);
```

#### Common Use Cases

✅ Discovery adapters building health check connections
✅ Connector factories parsing configuration
✅ Test fixtures generating test database connections
✅ Local development connection string generation

**Full Documentation**: [Framework Utilities Guide](../../../docs/guides/framework-utilities.md#connectionstringparser)

---

### ServiceDiscoveryAdapterBase (Template Method Base Class)

**File**: `ServiceDiscoveryAdapterBase.cs`
**Pattern**: Template method with container/local/Aspire detection
**When to Use**: Creating new discovery adapters for databases, caches, message queues, etc.

#### What It Provides

- ✅ Container environment detection (Docker/Podman)
- ✅ Aspire environment detection
- ✅ Local service detection with port scanning
- ✅ Configuration-based fallback
- ✅ Health validation framework
- ✅ Service attribute reading

#### Quick Example

```csharp
using Koan.Core.Orchestration;

internal sealed class PostgresDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "postgres";
    public override string[] Aliases => new[] { "postgresql", "npgsql" };

    protected override Type GetFactoryType() => typeof(PostgresAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(
        string serviceUrl,
        DiscoveryContext context,
        CancellationToken cancellationToken)
    {
        // Implement provider-specific health check
        using var connection = new NpgsqlConnection(serviceUrl);
        await connection.OpenAsync(cancellationToken);
        return true;
    }
}
```

#### What You Implement

1. `ServiceName` - Primary service identifier
2. `Aliases` - Alternative names (optional)
3. `GetFactoryType()` - Points to your adapter factory
4. `ValidateServiceHealth()` - Provider-specific connection test

**The base class handles all the discovery logic** - you just validate connectivity!

**Full Documentation**: [Framework Utilities Guide](../../../docs/guides/framework-utilities.md#servicediscoveryadapterbase)

---

## 📚 Related

- **ADR**: [ARCH-0068 - Refactoring Strategy](../../../docs/decisions/ARCH-0068-refactoring-strategy-static-vs-di.md)
- **Examples**: See `src/Connectors/Data/*/Discovery/` for 12 implementations
- **Tests**: `tests/Suites/Core/Unit/Koan.Tests.Core.Unit/Orchestration/ConnectionStringParserTests.cs`

---

## ❓ When to Use What

| Scenario | Use This |
|----------|----------|
| Parsing connection strings | `ConnectionStringParser` static methods |
| Building connection strings | `ConnectionStringParser` static methods |
| Creating discovery adapter | Inherit `ServiceDiscoveryAdapterBase` |
| Custom orchestration logic | Implement `IServiceDiscoveryAdapter` directly |

---

**Last Updated**: 2025-11-03

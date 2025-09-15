# Pillars Reference

**Document Type**: Reference Documentation (REF)  
**Target Audience**: Developers, Architects, AI Agents  
**Last Updated**: 2025-01-10  
**Framework Version**: v0.2.18+

---

## 🏛️ Koan Framework Pillars Reference

This document provides detailed reference information for all Koan Framework pillars, their capabilities, APIs, and configuration options.

---

## 🧱 Pillar Overview

| Pillar            | Package              | Purpose                                      | Dependencies          |
| ----------------- | -------------------- | -------------------------------------------- | --------------------- |
| **Core**          | `Koan.Core`          | Foundation, auto-registration, health checks | None                  |
| **Web**           | `Koan.Web`           | HTTP, controllers, authentication            | Core                  |
| **Data**          | `Koan.Data.*`        | Database abstraction, CQRS                   | Core                  |
| **Storage**       | `Koan.Storage`       | File/blob handling                           | Core                  |
| **Media**         | `Koan.Media`         | Media processing, HTTP endpoints             | Core, Storage         |
| **Messaging**     | `Koan.Messaging.*`   | Message queues, event handling               | Core                  |
| **AI**            | `Koan.AI`            | Chat, embeddings, vector search              | Core                  |
| **Flow**          | `Koan.Flow`          | Data pipeline, identity mapping              | Core, Data, Messaging |
| **Recipes**       | `Koan.Recipe.*`      | Best-practice bundles                        | Core                  |
| **Orchestration** | `Koan.Orchestration` | DevHost CLI, container management            | Core                  |
| **Scheduling**    | `Koan.Scheduling`    | Background jobs, startup tasks               | Core                  |

---

## 🏗️ Core Pillar

### Package: `Koan.Core`

The foundational layer providing auto-registration, configuration, and health checks.

#### Key Classes

```csharp
// Auto-Registration
public interface IKoanAutoRegistrar
{
    string ModuleName { get; }
    string? ModuleVersion { get; }
    void Initialize(IServiceCollection services);
    string Describe() => $"Module: {ModuleName} v{ModuleVersion}";
}

// Health Checks
public interface IHealthContributor
{
    string Name { get; }
    bool IsCritical { get; }
    Task<HealthReport> CheckAsync(CancellationToken cancellationToken = default);
}

public enum HealthStatus { Healthy, Degraded, Unhealthy }

public class HealthReport
{
    public HealthStatus Status { get; set; }
    public string? Description { get; set; }
    public Exception? Exception { get; set; }

    public static HealthReport Healthy(string? description = null);
    public static HealthReport Degraded(string? description = null, Exception? exception = null);
    public static HealthReport Unhealthy(string? description = null, Exception? exception = null);
}

// Environment Helpers
public static class KoanEnv
{
    public static string Environment { get; }
    public static bool IsProduction { get; }
    public static bool IsDevelopment { get; }
    public static bool IsTesting { get; }
    public static bool IsContainer { get; }
}

// Configuration Helpers
public static class KoanConfiguration
{
    public static T Read<T>(IConfiguration configuration, string key) where T : new();
    public static T ReadFirst<T>(IConfiguration configuration, params string[] keys) where T : new();
}
```

#### Configuration

```json
{
  "Koan": {
    "Core": {
      "EnableBootReport": true,
      "EnableTelemetry": false,
      "Environment": "Development"
    }
  }
}
```

#### Usage

```csharp
// Auto-registration
builder.Services.AddKoan(); // Discovers all IKoanAutoRegistrar implementations

// Custom health contributor
public class DatabaseHealthCheck : IHealthContributor
{
    public string Name => "database";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        try
        {
            // Check database connectivity
            await CheckDatabaseConnection();
            return HealthReport.Healthy("Database is responsive");
        }
        catch (Exception ex)
        {
            return HealthReport.Unhealthy("Database connection failed", ex);
        }
    }
}
```

---

## 🌐 Web Pillar

### Package: `Koan.Web`

HTTP layer with controllers, security, and authentication.

#### Key Classes

```csharp
// Entity Controllers
public abstract class EntityController<T> : ControllerBase where T : class, IEntity, new()
{
    [HttpGet]
    public virtual async Task<ActionResult<T[]>> Get([FromQuery] string? set = null);

    [HttpGet("{id}")]
    public virtual async Task<ActionResult<T>> Get(string id, [FromQuery] string? set = null);

    [HttpPost]
    public virtual async Task<IActionResult> Post([FromBody] T entity, [FromQuery] string? set = null);

    [HttpPut("{id}")]
    public virtual async Task<IActionResult> Put(string id, [FromBody] T entity, [FromQuery] string? set = null);

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete(string id, [FromQuery] string? set = null);
}

// Payload Transformers
public interface IPayloadTransformer<T>
{
    Task<object> TransformRequest(T input, TransformContext context);
    Task<object> TransformResponse(T output, TransformContext context);
}

// Authentication
public interface IAuthProvider
{
    string Name { get; }
    Task<AuthResult> AuthenticateAsync(AuthRequest request);
    Task<AuthResult> RefreshAsync(string refreshToken);
    Task SignOutAsync(string token);
}

// Web Capabilities
public interface IWebCapability<T> where T : IEntity
{
    string Name { get; }
    bool CanServe(Type entityType);
    Task<IActionResult> ExecuteAsync(string action, T entity, HttpContext context);
}
```

#### Configuration

```json
{
  "Koan": {
    "Web": {
      "EnableSwagger": true,
      "CorsOrigins": ["http://localhost:3000"],
      "SecureHeaders": {
        "EnableHsts": true,
        "HstsMaxAge": "31536000",
        "ContentSecurityPolicy": "default-src 'self'"
      },
      "Auth": {
        "ReturnUrl": {
          "DefaultPath": "/dashboard",
          "AllowList": ["/admin", "/profile"]
        },
        "RateLimit": {
          "ChallengesPerMinutePerIp": 10,
          "CallbackFailuresPer10MinPerIp": 5
        },
        "Providers": {
          "google": {
            "ClientId": "{GOOGLE_CLIENT_ID}",
            "ClientSecret": "{GOOGLE_CLIENT_SECRET}"
          },
          "microsoft": {
            "ClientId": "{MS_CLIENT_ID}",
            "ClientSecret": "{MS_CLIENT_SECRET}"
          },
          "discord": {
            "ClientId": "{DISCORD_CLIENT_ID}",
            "ClientSecret": "{DISCORD_CLIENT_SECRET}",
            "Scopes": ["identify", "email", "guilds"]
          },
          "corporate-saml": {
            "Type": "saml",
            "EntityId": "https://myapp.com/auth/corporate-saml/saml/metadata",
            "IdpMetadataUrl": "https://sso.company.com/metadata",
            "DisplayName": "Corporate SSO"
          }
        }
      }
    }
  }
}
```

#### Well-Known Endpoints

- `GET /.well-known/auth/providers` - Available auth providers
- `GET /.well-known/Koan/capabilities` - Framework capabilities
- `POST /auth/challenge/{provider}` - Authentication challenge
- `POST /auth/callback` - Authentication callback
- `POST /auth/logout` - Sign out

#### Usage

```csharp
// Simple entity controller
[Route("api/[controller]")]
public class ProductsController : EntityController<Product>
{
    // Inherits full REST API

    // Add custom endpoints
    [HttpGet("featured")]
    public Task<Product[]> GetFeatured() => Product.Featured();
}

// Custom transformer
public class ProductTransformer : IPayloadTransformer<Product>
{
    public Task<object> TransformRequest(Product input, TransformContext context)
    {
        // Transform incoming product data
        return Task.FromResult<object>(input);
    }

    public Task<object> TransformResponse(Product output, TransformContext context)
    {
        // Transform outgoing product data
        return Task.FromResult<object>(new
        {
            output.Id,
            output.Name,
            output.Price,
            Url = $"/products/{output.Id}"
        });
    }
}

// Authorization
[Route("api/[controller]")]
[Authorize]
public class OrdersController : EntityController<Order>
{
    [HttpPost]
    [Authorize(Policy = "CanCreateOrders")]
    public override Task<IActionResult> Post([FromBody] Order entity)
    {
        return base.Post(entity);
    }
}
```

---

## 💾 Data Pillar

### Packages: `Koan.Data.Core`, `Koan.Data.Sqlite`, `Koan.Data.Postgres`, `Koan.Data.MongoDB`, etc.

Unified data access across multiple database providers.

#### Key Classes

```csharp
// Entity Base
public abstract class Entity<T> : IEntity where T : class, new()
{
    public string Id { get; set; } = Ulid.NewUlid().ToString();
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;

    // Static methods (first-class)
    public static Task<T[]> All(CancellationToken ct = default);
    public static IQueryable<T> Query();
    public static Task<T?> ByIdAsync(string id, CancellationToken ct = default);
    public static Task<T[]> Where(Expression<Func<T, bool>> predicate);
    public static Task<PagedResult<T>> Page(int pageNumber = 1, int pageSize = 20);
    public static IAsyncEnumerable<T> AllStream(CancellationToken ct = default);

    // Instance methods
    public Task SaveAsync(CancellationToken ct = default);
    public Task DeleteAsync(CancellationToken ct = default);
}

// Data Provider
public interface IDataProvider
{
    string Name { get; }
    int Priority { get; }
    bool CanServe(Type entityType);
    Task<IDataAdapter<T>> GetAdapterAsync<T>() where T : IEntity;
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}

// Vector Support
[AttributeUsage(AttributeTargets.Property)]
public class VectorFieldAttribute : Attribute
{
    public int Dimensions { get; set; } = 1536; // OpenAI embedding dimensions
    public string IndexName { get; set; } = "";
}

public static class Vector<T> where T : IEntity
{
    public static Task<T[]> SearchAsync(string query, int limit = 10, CancellationToken ct = default);
    public static Task<T[]> SearchAsync(float[] embedding, int limit = 10, CancellationToken ct = default);
    public static Task IndexAsync(T entity, CancellationToken ct = default);
    public static Task DeleteFromIndexAsync(string id, CancellationToken ct = default);
}

// CQRS Support
public abstract class AggregateRoot<T> : Entity<T> where T : class, new()
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

// Direct SQL Access
public static class Data<T, TKey> where T : class, IEntity<TKey>
{
    public static Task<T[]> QueryAsync(string sql, object? parameters = null, CancellationToken ct = default);
    public static Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken ct = default);
    public static Task<TResult[]> QueryAsync<TResult>(string sql, object? parameters = null, CancellationToken ct = default);
}
```

#### Available Providers

| Provider       | Package               | Capabilities                      | Production Ready |
| -------------- | --------------------- | --------------------------------- | ---------------- |
| **SQLite**     | `Koan.Data.Sqlite`    | Full SQL, JSON, FTS               | ✅               |
| **PostgreSQL** | `Koan.Data.Postgres`  | Full SQL, JSON, Vector (pgvector) | ✅               |
| **SQL Server** | `Koan.Data.SqlServer` | Full SQL, JSON                    | ✅               |
| **MongoDB**    | `Koan.Data.MongoDB`   | Document, Aggregation             | ✅               |
| **Redis**      | `Koan.Data.Redis`     | Key-Value, Vector (HNSW)          | ✅               |
| **JSON File**  | `Koan.Data.Json`      | File-based JSON                   | ✅ Dev only      |
| **Weaviate**   | `Koan.Data.Weaviate`  | Vector, GraphQL                   | ✅               |

#### Configuration

```json
{
  "Koan": {
    "Data": {
      "DefaultProvider": "Postgres",
      "EnableQueryLogging": false,
      "Sqlite": {
        "ConnectionString": "Data Source=app.db",
        "EnableWAL": true
      },
      "Postgres": {
        "ConnectionString": "Host=localhost;Database=myapp;Username=user;Password=pass",
        "EnableArraySupport": true
      },
      "MongoDB": {
        "ConnectionString": "mongodb://localhost:27017",
        "DatabaseName": "myapp"
      },
      "Redis": {
        "ConnectionString": "localhost:6379",
        "Database": 0
      },
      "Vector": {
        "DefaultProvider": "Redis",
        "Redis": {
          "IndexPrefix": "Koan:",
          "DefaultDimensions": 1536
        }
      }
    }
  }
}
```

#### Usage

```csharp
// Basic entity
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public bool IsActive { get; set; } = true;

    // Vector field for semantic search
    [VectorField]
    public float[] DescriptionEmbedding { get; set; } = [];

    // Business methods
    public static Task<Product[]> InCategory(string category) =>
        Query().Where(p => p.Category == category && p.IsActive);

    public static Task<Product[]> OnSale() =>
        Query().Where(p => p.Price < p.OriginalPrice);

    // Semantic search
    public static Task<Product[]> SimilarTo(string description) =>
        Vector<Product>.SearchAsync(description, 10);
}

// Advanced querying
var products = await Product
    .Query()
    .Where(p => p.Price > 100 && p.Category == "Electronics")
    .OrderBy(p => p.Name)
    .Take(20)
    .ToArrayAsync();

// Streaming large datasets
await foreach (var product in Product.AllStream())
{
    await ProcessProduct(product);
}

// Direct SQL when needed
var customResults = await Data<Product, string>.QueryAsync(@"
    SELECT p.*, c.Name as CategoryName
    FROM Products p
    JOIN Categories c ON p.Category = c.Id
    WHERE p.Price > @minPrice",
    new { minPrice = 100 });
```

---

## 🗄️ Storage Pillar

### Package: `Koan.Storage`

File and blob storage with profile-based routing.

#### Key Classes

```csharp
// Storage Service
public interface IStorageService
{
    Task<StorageResult> CreateAsync(string key, Stream content, StorageOptions? options = null);
    Task<StorageResult> CreateAsync(string key, string content, StorageOptions? options = null);
    Task<StorageResult> CreateAsync(string key, byte[] content, StorageOptions? options = null);

    Task<Stream> ReadAsync(string key, string? profile = null);
    Task<byte[]> ReadBytesAsync(string key, string? profile = null);
    Task<string> ReadTextAsync(string key, string? profile = null);

    Task<bool> ExistsAsync(string key, string? profile = null);
    Task<StorageMetadata> HeadAsync(string key, string? profile = null);

    Task DeleteAsync(string key, string? profile = null);
    Task<StorageResult> CopyAsync(string sourceKey, string destinationKey, string? sourceProfile = null, string? destinationProfile = null);
    Task<StorageResult> MoveAsync(string sourceKey, string destinationKey, string? sourceProfile = null, string? destinationProfile = null);
}

// Storage Entity Pattern
public abstract class StorageEntity<T> where T : StorageEntity<T>, new()
{
    [StorageKey]
    public string Key { get; set; } = "";

    public string ContentType { get; set; } = "application/octet-stream";
    public long ContentLength { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;

    // Static creators
    public static async Task<T> CreateAsync(string key, Stream content);
    public static async Task<T> CreateAsync(string key, string content);
    public static async Task<T> CreateAsync(string key, byte[] content);

    // Instance operations
    public Task<Stream> ReadAsync();
    public Task<string> ReadTextAsync();
    public Task<byte[]> ReadBytesAsync();
    public Task DeleteAsync();
    public Task<T> CopyToAsync(string destinationKey);
    public Task<T> MoveToAsync(string destinationKey);
}

// Storage Providers
public interface IStorageProvider
{
    string Name { get; }
    Task<Stream> ReadAsync(string key, CancellationToken ct = default);
    Task WriteAsync(string key, Stream content, StorageOptions options, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}
```

#### Available Providers

| Provider         | Package                    | Capabilities                           | Production Ready |
| ---------------- | -------------------------- | -------------------------------------- | ---------------- |
| **Local File**   | `Koan.Storage.Local`       | Filesystem, range reads, atomic writes | ✅               |
| **Azure Blob**   | `Koan.Storage.Azure`       | Blob storage, CDN, presigned URLs      | 🚧 Planned       |
| **AWS S3**       | `Koan.Storage.S3`          | Object storage, presigned URLs         | 🚧 Planned       |
| **Google Cloud** | `Koan.Storage.GoogleCloud` | Cloud storage, CDN                     | 🚧 Planned       |

#### Configuration

```json
{
  "Koan": {
    "Storage": {
      "DefaultProfile": "Default",
      "Profiles": {
        "Default": {
          "Provider": "Local",
          "BasePath": "./storage"
        },
        "Hot": {
          "Provider": "Local",
          "BasePath": "./storage/hot"
        },
        "Cold": {
          "Provider": "Local",
          "BasePath": "./storage/cold"
        },
        "CDN": {
          "Provider": "Azure",
          "ConnectionString": "{AZURE_STORAGE_CONNECTION}",
          "Container": "cdn"
        }
      }
    }
  }
}
```

#### Usage

```csharp
// Direct storage operations
public class DocumentService
{
    private readonly IStorageService _storage;

    public async Task<string> UploadDocument(Stream content, string fileName)
    {
        var key = $"documents/{Ulid.NewUlid()}/{fileName}";
        var result = await _storage.CreateAsync(key, content);
        return result.Key;
    }

    public async Task<Stream> DownloadDocument(string key)
    {
        return await _storage.ReadAsync(key);
    }
}

// Storage entity pattern
[StorageBinding(Profile = "Documents")]
public class Document : StorageEntity<Document>
{
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string[] Tags { get; set; } = [];

    // Static creators with profile
    public static async Task<Document> UploadAsync(string title, Stream content)
    {
        var key = $"documents/{Ulid.NewUlid()}/{title}";
        return await CreateAsync(key, content);
    }
}

// Multi-profile usage
await _storage.CreateAsync("temp/file.txt", content); // Default profile
await _storage.CreateAsync("archive/file.txt", content, new StorageOptions { Profile = "Cold" });

// Server-side operations (when supported)
await _storage.CopyAsync("temp/file.txt", "permanent/file.txt",
    sourceProfile: "Default", destinationProfile: "Hot");
```

---

## 📺 Media Pillar

### Package: `Koan.Media`

First-class media handling with HTTP endpoints and transforms.

#### Key Classes

```csharp
// Media Object
public abstract class MediaObject<T> : StorageEntity<T> where T : MediaObject<T>, new()
{
    public string OriginalFileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public MediaType MediaType { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();

    // Relationship tracking
    public string? SourceMediaId { get; set; }
    public MediaRelationshipType? RelationshipType { get; set; }

    // Static API
    public static Task<T> UploadAsync(Stream content, string fileName, MediaUploadOptions? options = null);
    public static Task<T> UploadAsync(IFormFile file, MediaUploadOptions? options = null);
    public static Task<T> UploadAsync(byte[] content, string fileName, string mimeType, MediaUploadOptions? options = null);

    public static Task<T?> GetAsync(string id);
    public static Task<string> UrlAsync(string id, MediaUrlOptions? options = null);

    // Instance operations
    public Task<T[]> GetDerivativesAsync();
    public Task<T[]> GetVariantsAsync();
    public Task<T> CreateDerivativeAsync(string transformKey, object? parameters = null);
    public Task RunTransformAsync(string transformKey, object? parameters = null);
    public IAsyncEnumerable<TransformProgress> StreamTransformAsync(string transformKey, object? parameters = null);
}

// Transform Pipeline
public interface IMediaTransform
{
    string Name { get; }
    string[] SupportedTypes { get; }
    Task<MediaTransformResult> TransformAsync(MediaTransformRequest request);
}

public class MediaTransformRequest
{
    public Stream SourceStream { get; set; } = null!;
    public string SourceMimeType { get; set; } = "";
    public string TargetMimeType { get; set; } = "";
    public Dictionary<string, object> Parameters { get; set; } = new();
}

// HTTP Endpoints (Auto-registered)
[Route("media")]
public class MediaController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetMedia(string id, [FromQuery] MediaGetOptions? options = null);

    [HttpHead("{id}")]
    public async Task<IActionResult> HeadMedia(string id);

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] MediaUploadOptions? options = null);
}
```

#### Built-in Transforms

| Transform     | Parameters                    | Description             |
| ------------- | ----------------------------- | ----------------------- |
| **resize**    | `width`, `height`, `mode`     | Image resizing          |
| **rotate**    | `angle`                       | Image rotation          |
| **crop**      | `x`, `y`, `width`, `height`   | Image cropping          |
| **format**    | `format`                      | Format conversion       |
| **quality**   | `quality`                     | JPEG quality adjustment |
| **watermark** | `text`, `position`, `opacity` | Text watermarking       |

#### Configuration

```json
{
  "Koan": {
    "Media": {
      "DefaultStorageProfile": "Media",
      "MaxUploadSize": 10485760,
      "AllowedMimeTypes": [
        "image/jpeg",
        "image/png",
        "image/gif",
        "video/mp4",
        "application/pdf"
      ],
      "Transforms": {
        "thumbnail": {
          "Transform": "resize",
          "Parameters": {
            "width": 200,
            "height": 200,
            "mode": "crop"
          }
        },
        "hero": {
          "Transform": "resize",
          "Parameters": {
            "width": 1200,
            "height": 600,
            "mode": "cover"
          }
        }
      }
    }
  }
}
```

#### Usage

```csharp
// Upload media
public class ProductService
{
    public async Task<Product> CreateProductWithImage(CreateProductRequest request, IFormFile imageFile)
    {
        // Upload original image
        var image = await ProductImage.UploadAsync(imageFile, new MediaUploadOptions
        {
            StorageProfile = "ProductImages"
        });

        // Create thumbnail derivative
        var thumbnail = await image.CreateDerivativeAsync("thumbnail");

        var product = new Product
        {
            Name = request.Name,
            Price = request.Price,
            ImageId = image.Id,
            ThumbnailId = thumbnail.Id
        };

        await product.SaveAsync();
        return product;
    }
}

// Custom media entity
[MediaBinding(StorageProfile = "ProductImages")]
public class ProductImage : MediaObject<ProductImage>
{
    public string ProductId { get; set; } = "";
    public bool IsHeroImage { get; set; }

    public static Task<ProductImage[]> ForProduct(string productId) =>
        Query().Where(i => i.ProductId == productId);
}

// HTTP endpoints (automatic)
// GET /media/{id} - Stream media with range support
// GET /media/{id}?w=200&h=200 - Resized image
// HEAD /media/{id} - Metadata only
// POST /media/upload - Upload endpoint
```

---

## 📨 Messaging Pillar

### Packages: `Koan.Messaging.Core`, `Koan.Messaging.RabbitMq`, `Koan.Messaging.Redis`

Reliable messaging with multiple transport support.

#### Key Classes

```csharp
// Message Bus
public interface IBus
{
    Task SendAsync<T>(T message, MessageOptions? options = null, CancellationToken ct = default);
    Task SendBatchAsync<T>(IEnumerable<T> messages, MessageOptions? options = null, CancellationToken ct = default);
    IDisposable Subscribe<T>(Func<T, Task> handler, SubscriptionOptions? options = null) where T : class;
}

// Message Extensions
public static class MessageExtensions
{
    public static async Task Send<T>(this T message, MessageOptions? options = null, CancellationToken ct = default);
    public static Task On<T>(this object subscriber, Func<T, Task> handler, SubscriptionOptions? options = null);
}

// Message Options
public class MessageOptions
{
    public string? RoutingKey { get; set; }
    public Dictionary<string, object> Headers { get; set; } = new();
    public TimeSpan? Delay { get; set; }
    public int? Priority { get; set; }
    public bool Persistent { get; set; } = true;
}

public class SubscriptionOptions
{
    public string? QueueName { get; set; }
    public bool AutoAck { get; set; } = true;
    public int PrefetchCount { get; set; } = 1;
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}

// Transport Provider
public interface IMessageTransport
{
    string Name { get; }
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task SendAsync<T>(T message, MessageOptions options, CancellationToken ct = default);
    Task<IDisposable> SubscribeAsync<T>(Func<T, Task> handler, SubscriptionOptions options, CancellationToken ct = default);
}
```

#### Available Transports

| Transport     | Package                   | Capabilities               | Production Ready |
| ------------- | ------------------------- | -------------------------- | ---------------- |
| **RabbitMQ**  | `Koan.Messaging.RabbitMq` | Full AMQP, DLQ, clustering | ✅               |
| **Redis**     | `Koan.Messaging.Redis`    | Pub/sub, streams           | ✅               |
| **In-Memory** | `Koan.Messaging.Core`     | Testing, development       | ✅ Dev only      |

#### Configuration

```json
{
  "Koan": {
    "Messaging": {
      "DefaultTransport": "RabbitMq",
      "RabbitMq": {
        "ConnectionString": "amqp://guest:guest@localhost:5672",
        "VirtualHost": "/",
        "ExchangeName": "Koan.events",
        "EnableDeadLetterQueue": true,
        "MaxRetries": 3
      },
      "Redis": {
        "ConnectionString": "localhost:6379",
        "Database": 0,
        "StreamMaxLength": 10000
      }
    }
  }
}
```

#### Usage

```csharp
// Domain events
public class OrderCreated
{
    public string OrderId { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Publishing events
public class OrderService
{
    public async Task<Order> CreateOrder(CreateOrderRequest request)
    {
        var order = new Order { /* ... */ };
        await order.SaveAsync();

        // Publish domain event
        await new OrderCreated
        {
            OrderId = order.Id,
            CustomerEmail = order.CustomerEmail,
            Total = order.Total
        }.Send();

        return order;
    }
}

// Event handlers
public class EmailService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Handle order events
        await this.On<OrderCreated>(async evt =>
        {
            await SendOrderConfirmation(evt.CustomerEmail, evt.OrderId);
        });

        // Keep service running
        await Task.Delay(Timeout.Infinite, ct);
    }
}

// Batch processing
public class ReportService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var orders = new List<OrderCreated>();

        await this.On<OrderCreated>(async evt =>
        {
            orders.Add(evt);

            // Process in batches of 10
            if (orders.Count >= 10)
            {
                await ProcessOrderBatch(orders.ToArray());
                orders.Clear();
            }
        });
    }
}
```

---

## 🤖 AI Pillar

### Package: `Koan.AI`

AI capabilities with chat, embeddings, and vector search.

#### Key Classes

```csharp
// AI Interface
public interface IAi
{
    Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<AiChatStreamChunk> ChatStreamAsync(AiChatRequest request, CancellationToken ct = default);
    Task<AiEmbeddingResponse> EmbedAsync(AiEmbeddingRequest request, CancellationToken ct = default);
}

// Chat Models
public class AiChatRequest
{
    public AiMessage[] Messages { get; set; } = [];
    public string Model { get; set; } = "";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1000;
    public bool Stream { get; set; } = false;
}

public class AiMessage
{
    public AiMessageRole Role { get; set; }
    public string Content { get; set; } = "";
}

public enum AiMessageRole { System, User, Assistant }

// Provider Interface
public interface IAiProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default);
    Task<AiEmbeddingResponse> EmbedAsync(AiEmbeddingRequest request, CancellationToken ct = default);
}

// Budget Management
public interface IAiBudgetManager
{
    Task<bool> CanProcessRequestAsync(AiRequest request, CancellationToken ct = default);
    Task RecordUsageAsync(AiUsage usage, CancellationToken ct = default);
    Task<AiBudgetStatus> GetStatusAsync(CancellationToken ct = default);
}
```

#### Available Providers

| Provider         | Package                   | Capabilities           | Production Ready |
| ---------------- | ------------------------- | ---------------------- | ---------------- |
| **Ollama**       | `Koan.AI.Provider.Ollama` | Local LLMs, streaming  | ✅               |
| **OpenAI**       | `Koan.AI.Provider.OpenAI` | GPT models, embeddings | 🚧 Planned       |
| **Azure OpenAI** | `Koan.AI.Provider.Azure`  | Enterprise GPT         | 🚧 Planned       |

#### Configuration

```json
{
  "Koan": {
    "AI": {
      "DefaultProvider": "Ollama",
      "Budget": {
        "MaxTokensPerRequest": 2000,
        "MaxRequestsPerMinute": 60,
        "MaxCostPerDay": 100.0
      },
      "Ollama": {
        "BaseUrl": "http://localhost:11434",
        "DefaultModel": "llama2",
        "DefaultEmbeddingModel": "nomic-embed-text"
      },
      "OpenAI": {
        "ApiKey": "{OPENAI_API_KEY}",
        "DefaultModel": "gpt-4",
        "DefaultEmbeddingModel": "text-embedding-3-small"
      }
    }
  }
}
```

#### Usage

```csharp
// Basic chat
public class ChatService
{
    private readonly IAi _ai;

    public async Task<string> GetResponse(string userMessage)
    {
        var request = new AiChatRequest
        {
            Messages = [
                new() { Role = AiMessageRole.System, Content = "You are a helpful assistant." },
                new() { Role = AiMessageRole.User, Content = userMessage }
            ],
            MaxTokens = 500
        };

        var response = await _ai.ChatAsync(request);
        return response.Choices?.FirstOrDefault()?.Message?.Content ?? "";
    }
}

// Streaming chat
[Route("api/[controller]")]
[ApiController]
public class ChatController : ControllerBase
{
    private readonly IAi _ai;

    [HttpPost("stream")]
    public async Task StreamChat([FromBody] ChatRequest request)
    {
        Response.Headers.Add("Content-Type", "text/event-stream");

        var aiRequest = new AiChatRequest
        {
            Messages = request.Messages,
            Stream = true
        };

        await foreach (var chunk in _ai.ChatStreamAsync(aiRequest))
        {
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n");
            await Response.Body.FlushAsync();
        }
    }
}

// Embeddings and vector search
public class DocumentService
{
    private readonly IAi _ai;

    public async Task<Document> CreateDocument(string title, string content)
    {
        // Generate embedding
        var embeddingRequest = new AiEmbeddingRequest { Input = content };
        var embeddingResponse = await _ai.EmbedAsync(embeddingRequest);

        var document = new Document
        {
            Title = title,
            Content = content,
            ContentEmbedding = embeddingResponse.Embeddings.FirstOrDefault()?.Vector ?? []
        };

        await document.SaveAsync();
        return document;
    }

    public async Task<Document[]> SearchSimilar(string query, int limit = 5)
    {
        return await Document.SimilarTo(query, limit);
    }
}
```

---

## 🌊 Flow Pillar

### Package: `Koan.Flow`

Data pipeline for ingestion, transformation, and identity resolution.

#### Key Classes

```csharp
// Flow Entity
public abstract class FlowEntity<T> : Entity<T> where T : class, new()
{
    // Aggregation keys for identity resolution
}

[AttributeUsage(AttributeTargets.Property)]
public class AggregationKeyAttribute : Attribute { }

// Flow Adapter
[AttributeUsage(AttributeTargets.Class)]
public class FlowAdapterAttribute : Attribute
{
    public string System { get; set; } = "";
    public string Adapter { get; set; } = "";
}

// Interceptors
public static class FlowInterceptors
{
    public static FlowInterceptorBuilder<T> For<T>() where T : IFlowEntity;
}

public class FlowInterceptorBuilder<T>
{
    public FlowInterceptorBuilder<T> BeforeIntake(Func<T, Task<FlowIntakeAction>> interceptor);
    public FlowInterceptorBuilder<T> AfterIntake(Func<T, Task<FlowStageAction>> interceptor);
    public FlowInterceptorBuilder<T> BeforeKeying(Func<T, Task<FlowStageAction>> interceptor);
    public FlowInterceptorBuilder<T> AfterKeying(Func<T, Task<FlowStageAction>> interceptor);
    public FlowInterceptorBuilder<T> BeforeAssociation(Func<T, Task<FlowStageAction>> interceptor);
    public FlowInterceptorBuilder<T> AfterAssociation(Func<T, Task<FlowStageAction>> interceptor);
    public FlowInterceptorBuilder<T> BeforeProjection(Func<T, Task<FlowStageAction>> interceptor);
    public FlowInterceptorBuilder<T> AfterProjection(Func<T, Task<FlowStageAction>> interceptor);

    // Conditional interceptors
    public FlowInterceptorBuilder<T> OnAssociationSuccess(Func<T, Task<FlowStageAction>> interceptor);
    public FlowInterceptorBuilder<T> OnAssociationFailure(Func<T, Task<FlowStageAction>> interceptor);
}

// Flow Actions
public static class FlowIntakeActions
{
    public static FlowIntakeAction Continue(IFlowEntity entity);
    public static FlowIntakeAction Drop(IFlowEntity entity, string? reason = null);
    public static FlowIntakeAction Park(IFlowEntity entity, string reasonCode, string? evidence = null);
}

public static class FlowStageActions
{
    public static FlowStageAction Continue(IFlowEntity entity);
    public static FlowStageAction Skip(IFlowEntity entity, string? reason = null);
    public static FlowStageAction Defer(IFlowEntity entity, TimeSpan delay, string? reason = null);
    public static FlowStageAction Park(IFlowEntity entity, string reasonCode, string? evidence = null);
}

// External ID Policy
[AttributeUsage(AttributeTargets.Class)]
public class FlowPolicyAttribute : Attribute
{
    public ExternalIdPolicy ExternalIdPolicy { get; set; } = ExternalIdPolicy.AutoPopulate;
    public string? ExternalIdKey { get; set; }
}

public enum ExternalIdPolicy
{
    AutoPopulate, // Framework auto-generates external IDs
    Manual,       // Developer provides external IDs
    Disabled,     // No external ID tracking
    SourceOnly    // Only track source, not individual IDs
}
```

#### Pipeline Stages

1. **Intake**: Raw data ingestion with validation
2. **Keying**: Extract aggregation keys from entities
3. **Association**: Link entities with same aggregation keys
4. **Projection**: Create canonical views of associated entities

#### Configuration

```json
{
  "Koan": {
    "Flow": {
      "EnableInterceptors": true,
      "ExternalIdPolicy": "AutoPopulate",
      "Pipeline": {
        "BatchSize": 100,
        "MaxRetries": 3,
        "RetryDelay": "00:00:05"
      }
    }
  }
}
```

#### Usage

```csharp
// Multi-source entity
[FlowPolicy(ExternalIdPolicy = ExternalIdPolicy.AutoPopulate)]
public class Product : FlowEntity<Product>
{
    [AggregationKey]
    public string SKU { get; set; } = "";

    public string Name { get; set; } = "";
    public decimal? Cost { get; set; }  // From ERP
    public decimal? Price { get; set; } // From E-commerce
    public string Category { get; set; } = "";
}

// Source adapters
[FlowAdapter(system: "erp", adapter: "sap")]
public class SapAdapter : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var product = new Product
        {
            SKU = "PROD-001",
            Name = "Widget",
            Cost = 50.00m,
            Category = "Widgets"
        };

        await product.Send(); // Sends through Flow pipeline
    }
}

[FlowAdapter(system: "ecommerce", adapter: "shopify")]
public class ShopifyAdapter : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var product = new Product
        {
            SKU = "PROD-001", // Same SKU - will associate
            Name = "Widget",
            Price = 75.00m,
            Category = "Widgets"
        };

        await product.Send();
    }
}

// Interceptors
public class ProductInterceptor : IKoanAutoRegistrar
{
    public void Initialize(IServiceCollection services)
    {
        FlowInterceptors
            .For<Product>()
            .BeforeIntake(async product =>
            {
                // Validate required fields
                if (string.IsNullOrEmpty(product.SKU))
                    return FlowIntakeActions.Drop(product, "Missing SKU");

                return FlowIntakeActions.Continue(product);
            })
            .AfterAssociation(async product =>
            {
                // Notify systems after association
                await NotifyInventorySystem(product);
                return FlowStageActions.Continue(product);
            });
    }
}

// Canonical result has data from both sources:
// {
//   "sku": "PROD-001",
//   "name": "Widget",
//   "cost": 50.00,      // From ERP
//   "price": 75.00,     // From E-commerce
//   "category": "Widgets",
//   "identifier": {
//     "external": {
//       "erp": "PROD-001",
//       "ecommerce": "PROD-001"
//     }
//   }
// }
```

---

## 🎯 Usage Summary

Each pillar provides:

1. **Zero Configuration**: Works out of the box with sensible defaults
2. **Progressive Enhancement**: Add complexity only when needed
3. **Consistent APIs**: Similar patterns across all pillars
4. **Escape Hatches**: Access to underlying providers when needed
5. **Production Ready**: Health checks, monitoring, error handling

Choose pillars based on your needs:

- **Core + Web + Data**: Basic web API
- **+ AI**: Add intelligence and vector search
- **+ Messaging**: Add event-driven patterns
- **+ Flow**: Add multi-source data pipeline
- **+ Storage/Media**: Add file/media handling

For detailed examples and advanced usage, see the pillar-specific documentation and samples in the repository.

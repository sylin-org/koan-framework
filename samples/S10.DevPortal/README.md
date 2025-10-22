# S10.DevPortal - Koan Framework Capabilities Demo

## Overview

S10.DevPortal is a comprehensive demonstration of Koan Framework's core capabilities, designed to showcase entity-first development, multi-provider transparency, zero-boilerplate controllers, bulk operations, and relationship navigation patterns.

**🎯 Primary Purpose:** Framework capability demonstration and developer onboarding

## 🚀 Quick Start

### Option 1: Docker Compose (Recommended)

```bash
# Navigate to the project directory
cd samples/S10.DevPortal

# Start the complete demo stack
./start.bat

# Open browser to http://localhost:5090
```

### Option 2: Local Development

```bash
# Ensure you have .NET 9.0 SDK installed
dotnet run

# Open browser to http://localhost:5000
```

## 🏗️ Architecture Overview

### Clean Koan Initialization

The entire application is initialized with minimal configuration:

```csharp
// Program.cs - Clean framework initialization
builder.Services.AddKoan()
    .AsWebApi()
    .AsProxiedApi();
```

This demonstrates Koan's **"Reference = Intent"** philosophy - adding package references automatically enables functionality.

### Entity-First Development

All domain models inherit from `Entity<T>` with automatic GUID v7 generation:

```csharp
public class Article : Entity<Article>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    // ID automatically generated as GUID v7 on first access
}
```

### Zero-Boilerplate Controllers

Controllers provide full CRUD functionality with minimal code:

```csharp
[Route("api/[controller]")]
public class ArticlesController : EntityController<Article>
{
    // Inherits all CRUD operations automatically:
    // GET /, POST /query, GET /{id}, POST /, DELETE /{id}, POST /bulk, etc.
}
```

## 🎮 Demo Features

### 1. **Multi-Provider Transparency**
- Switch between MongoDB, PostgreSQL, and SQLite without code changes
- Same API works across all storage backends
- Runtime provider switching demonstration

### 2. **Bulk Operations**
- Efficient bulk insert, update, and delete operations
- Performance comparison across providers
- Capability detection and fallback patterns

### 3. **Set Routing**
- Logical data partitioning with `?set=` parameter
- Published vs draft articles demonstration
- Same entity, different logical views

### 4. **Relationship Navigation**
- Parent/child hierarchical relationships
- Batch relationship loading
- Threaded comment system demonstration

### 5. **Capability Detection**
- Runtime detection of provider capabilities
- Graceful degradation for unsupported features
- Performance metrics and comparison

## 📦 Project Structure

```
S10.DevPortal/
├── Controllers/           # EntityController implementations
│   ├── ArticlesController.cs     # Bulk ops + set routing demo
│   ├── TechnologiesController.cs # Hierarchical relationships
│   ├── CommentsController.cs     # Threaded comments
│   ├── UsersController.cs        # Basic entity pattern
│   └── DemoController.cs         # Framework capabilities showcase
├── Models/               # Entity<T> domain models
│   ├── Article.cs        # Content with relationships
│   ├── Technology.cs     # Hierarchical self-references
│   ├── Comment.cs        # Threaded comments
│   └── User.cs          # Basic user entity
├── Services/            # Demo data seeding
│   ├── IDemoSeedService.cs
│   └── DemoSeedService.cs
├── wwwroot/             # AngularJS frontend
│   ├── index.html       # Single-page demo app
│   ├── js/              # Application logic
│   ├── css/             # Styling
│   └── views/           # Page templates
├── docker/              # Multi-provider demo stack
│   └── compose.yml      # MongoDB + PostgreSQL + Redis
├── Dockerfile           # Container build
├── start.bat           # Quick start script
└── README.md           # This file
```

## 🔧 Configuration

### Multi-Provider Setup

The application supports multiple storage providers configured via `appsettings.json`:

```json
{
  "Koan": {
    "Data": {
      "Mongo": {
        "Database": "DevPortal",
        "ConnectionString": "mongodb://localhost:5091/DevPortal"
      },
      "Postgres": {
        "ConnectionString": "Host=localhost;Port=5092;Database=devportal;Username=postgres;Password=dev"
      },
      "Sqlite": {
        "ConnectionString": "Data Source=./data/devportal.db"
      }
    }
  }
}
```

### Container Environment

When running in Docker, the application automatically detects the containerized environment and uses appropriate connection strings.

## 🌐 API Endpoints

### Framework Demo Endpoints

- `POST /api/demo/switch-provider/{provider}` - Switch storage provider
- `GET /api/demo/capabilities` - Get current provider capabilities
- `GET /api/demo/performance-comparison` - Compare provider performance
- `POST /api/demo/bulk-demo?count=100` - Bulk operations demonstration
- `POST /api/demo/seed-demo-data` - Seed demo data
- `DELETE /api/demo/clear-demo-data` - Clear all demo data

### Auto-Generated Entity Endpoints

Each `EntityController<T>` automatically provides:

- `GET /api/{entity}` - Collection with pagination, sorting, filtering
- `GET /api/{entity}?set={setName}` - Set routing
- `POST /api/{entity}/query` - Complex query with JSON filters
- `GET /api/{entity}/{id}` - Get by ID with relationship expansion
- `POST /api/{entity}` - Upsert entity
- `POST /api/{entity}/bulk` - Bulk upsert operations
- `DELETE /api/{entity}/{id}` - Delete by ID
- `DELETE /api/{entity}/bulk` - Bulk delete by IDs

### Custom Relationship Endpoints

- `GET /api/technologies/{id}/children` - Get child technologies
- `GET /api/technologies/{id}/hierarchy` - Full hierarchy view
- `GET /api/comments/thread/{articleId}` - Threaded comments

## 📊 Performance Features

### Streaming and Memory Efficiency

```csharp
// Memory-efficient streaming for large datasets
await foreach (var article in Article.AllStream(batchSize: 1000)) {
    await ProcessArticle(article);
}

// Batch relationship loading
var enrichedArticles = await articles.Relatives<Article, string>();
```

### Provider Capability Detection

```csharp
var capabilities = Data<Article, string>.QueryCaps;
if (capabilities.Capabilities.HasFlag(QueryCapabilities.LinqQueries)) {
    // Complex LINQ pushed down to provider
} else {
    // Automatic fallback to in-memory filtering
}
```

## 🧪 Demo Scenarios

### 1. Provider Switching Demo
1. Seed demo data
2. Switch to MongoDB provider
3. Verify same data appears
4. Switch to PostgreSQL
5. Compare performance metrics

### 2. Bulk Operations Demo
1. Generate 100+ sample articles
2. Bulk insert with timing
3. Bulk query with performance comparison
4. Bulk delete demonstration

### 3. Relationship Navigation
1. Create technology hierarchy
2. Navigate parent/child relationships
3. Demonstrate batch loading
4. Show relationship graph

### 4. Set Routing Demo
1. Create articles with mixed published/draft status
2. Switch between "all", "published", "drafts" views
3. Same entity, different logical sets

## 🚢 Deployment

### Docker Production Build

```bash
# Build production image
docker build -t s10-devportal .

# Run with external database
docker run -p 5090:5090 \
  -e Koan__Data__Mongo__ConnectionString="mongodb://your-mongo/DevPortal" \
  s10-devportal
```

### Container Orchestration

The provided `docker/compose.yml` includes:
- Application container with health checks
- MongoDB with persistent volume
- PostgreSQL with initialization
- Redis for caching (optional)
- Dedicated network for service communication

## 🎯 Learning Objectives

After exploring S10.DevPortal, developers will understand:

1. **Entity-First Development** - How `Entity<T>` provides automatic GUID v7 generation and relationship navigation
2. **Multi-Provider Transparency** - Same code working across SQL, NoSQL, and file storage
3. **Zero-Boilerplate APIs** - How `EntityController<T>` provides full CRUD with no manual implementation
4. **Bulk Operations** - Efficient handling of large datasets with provider-specific optimizations
5. **Relationship Navigation** - Parent/child and soft relationships with batch loading
6. **Set Routing** - Logical data organization and multi-tenancy patterns
7. **Capability Detection** - Runtime adaptation to provider capabilities
8. **Container-Native Development** - Framework integration with Docker and orchestration platforms

## 🔍 Framework Patterns Demonstrated

### Auto-Registration Pattern
```csharp
// Only need .AddKoan() - modules self-register
services.AddKoan();
// Anti-pattern: Manual service registration when framework provides auto-registration
```

### Provider Capability Detection
```csharp
var capabilities = Data<Order, string>.QueryCaps;
if (capabilities.Capabilities.HasFlag(QueryCapabilities.LinqQueries)) {
    // Provider supports complex LINQ
} else {
    // Automatic fallback patterns
}
```

### Relationship Navigation
```csharp
var parent = await order.GetParent<Customer>();
var children = await customer.GetChildren<Order>();
var fullGraph = await order.GetRelatives(); // All relationships
```

### Set-Scoped Operations
```csharp
using var _ = DataSetContext.With("backup");
var backupOrders = await Order.All();
```

## 🆚 Comparison with Other Samples

| Sample | Purpose | Complexity | Key Features |
|--------|---------|------------|--------------|
| **S1.Web** | Basic CRUD demo | Simple | Todo management, basic relationships |
| **S5.Recs** | AI + Vector search | Advanced | Recommendation engine, Weaviate, AI integration |
| **S10.DevPortal** | **Framework showcase** | **Demo-focused** | **Multi-provider, capability detection, live switching** |

S10.DevPortal is unique in providing **live demonstration** of framework capabilities rather than domain-specific functionality.

## 🎁 Success Criteria

S10.DevPortal successfully demonstrates Koan Framework when:

✅ **Same code works across MongoDB, PostgreSQL, SQLite** - Multi-provider transparency
✅ **EntityController provides full CRUD with zero boilerplate** - Entity-first development
✅ **Bulk operations handle 1000+ records efficiently** - Scalability demonstration
✅ **Set routing shows same entity, different logical views** - Data organization patterns
✅ **Relationship navigation works seamlessly** - Entity<T> relationship capabilities
✅ **Frontend can switch providers and show capability differences** - Live capability detection

## 📞 Support

For questions about Koan Framework or this demo:

- **Framework Documentation**: [Koan Framework Docs](../../docs/)
- **Other Samples**: Browse `samples/` directory for domain-specific examples
- **Issues**: Report bugs or request features via GitHub issues

---

**🎯 Next Steps**: Try the other samples to see domain-specific applications of these same patterns!
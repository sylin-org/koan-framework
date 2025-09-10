# REF_SORA_GETTING_STARTED.md

**Document Type**: Reference Documentation (REF)  
**Target Audience**: New Developers, AI Agents  
**Last Updated**: 2025-01-10  
**Framework Version**: v0.2.18+

---

## 🚀 Getting Started with Sora Framework

This guide will take you from zero to a running Sora application in under 10 minutes.

---

## 📋 Prerequisites

- **.NET 9 SDK** or later
- **IDE**: Visual Studio, VS Code, or JetBrains Rider
- **Optional**: Docker (for local dependencies)

---

## 🏃‍♂️ Quick Start (30 seconds)

### 1. Create New Project
```bash
mkdir my-sora-app
cd my-sora-app
dotnet new web
```

### 2. Add Sora Packages
```bash
# Core packages for a basic web API
dotnet add package Sora.Core
dotnet add package Sora.Web
dotnet add package Sora.Data.Sqlite
```

### 3. Create Your First Model
```csharp
// Models/Todo.cs
using Sora.Core;

namespace MyApp.Models;

public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    
    // Static methods are first-class citizens
    public static Task<Todo[]> Pending() => 
        All().Where(t => !t.IsCompleted);
        
    public static Task<Todo[]> Recent() => 
        All().Where(t => t.Created > DateTimeOffset.UtcNow.AddDays(-7));
}
```

### 4. Create Controller (Automatic REST API)
```csharp
// Controllers/TodosController.cs
using Microsoft.AspNetCore.Mvc;
using Sora.Web;
using MyApp.Models;

namespace MyApp.Controllers;

[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    // Automatically provides full REST API:
    // GET /api/todos
    // GET /api/todos/{id}  
    // POST /api/todos
    // PUT /api/todos/{id}
    // DELETE /api/todos/{id}
    
    // Add custom endpoints if needed
    [HttpGet("pending")]
    public Task<Todo[]> GetPending() => Todo.Pending();
    
    [HttpGet("recent")]  
    public Task<Todo[]> GetRecent() => Todo.Recent();
}
```

### 5. Configure Application
```csharp
// Program.cs
using Sora.Core;

var builder = WebApplication.CreateBuilder(args);

// Single line adds all referenced Sora modules
builder.Services.AddSora();

var app = builder.Build();

// Sora configures the pipeline automatically
await app.RunAsync();
```

### 6. Run Your Application
```bash
dotnet run
```

**That's it!** You now have:
- ✅ Full REST API with CRUD operations
- ✅ SQLite database (auto-created)
- ✅ Health endpoints at `/health`, `/health/live`, `/health/ready`
- ✅ Swagger UI at `/swagger` (development mode)
- ✅ Proper error handling and logging

---

## 🧪 Test Your API

Visit `http://localhost:5000/swagger` or use curl:

```bash
# Get all todos
curl http://localhost:5000/api/todos

# Create a todo
curl -X POST http://localhost:5000/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title": "Learn Sora Framework", "isCompleted": false}'

# Get pending todos
curl http://localhost:5000/api/todos/pending

# Check health
curl http://localhost:5000/health
```

---

## 📈 Level Up: Add More Features

### Add AI Capabilities
```bash
dotnet add package Sora.AI
dotnet add package Sora.AI.Provider.Ollama
```

```csharp
// Controllers/AiController.cs
[Route("api/[controller]")]
[ApiController]
public class AiController : ControllerBase
{
    private readonly IAi _ai;
    
    public AiController(IAi ai) => _ai = ai;
    
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] string message)
    {
        var request = new AiChatRequest
        {
            Messages = [new() { Role = AiMessageRole.User, Content = message }]
        };
        
        var response = await _ai.ChatAsync(request);
        return Ok(response.Choices?.FirstOrDefault()?.Message?.Content);
    }
}
```

### Add Messaging
```bash
dotnet add package Sora.Messaging.RabbitMq
```

```csharp
// Services/TodoService.cs
public class TodoService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Listen for todo events
        await this.On<Todo>(async todo =>
        {
            Console.WriteLine($"Todo received: {todo.Title}");
            // Process todo...
        });
    }
}

// Send messages anywhere in your app
await new Todo { Title = "Background task" }.Send();
```

### Add Vector Search
```bash
dotnet add package Sora.Data.Vector
dotnet add package Sora.Data.Redis  # Vector provider
```

```csharp
// Models/Document.cs
public class Document : Entity<Document>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    
    // Auto-vectorization
    [VectorField]
    public float[] ContentEmbedding { get; set; } = [];
    
    // Semantic search
    public static Task<Document[]> SimilarTo(string query, int limit = 10) =>
        Vector<Document>.SearchAsync(query, limit);
}
```

### Add GraphQL
```bash
dotnet add package Sora.Web.GraphQl
```

GraphQL schema is auto-generated from your models - no additional code needed!

---

## 🔧 Configuration

Sora follows .NET configuration patterns with intelligent defaults:

```json
{
  "Sora": {
    "Data": {
      "DefaultProvider": "Sqlite",
      "Sqlite": {
        "ConnectionString": "Data Source=app.db"
      }
    },
    "Web": {
      "EnableSwagger": true,
      "CorsOrigins": ["http://localhost:3000"]
    },
    "AI": {
      "DefaultProvider": "Ollama",
      "Ollama": {
        "BaseUrl": "http://localhost:11434"
      }
    },
    "Messaging": {
      "DefaultProvider": "InMemory"
    }
  }
}
```

Environment variables work too:
```bash
export SORA__DATA__DEFAULTPROVIDER=Postgres
export SORA__DATA__POSTGRES__CONNECTIONSTRING="Host=localhost;Database=myapp"
```

---

## 🐳 Local Development with Dependencies

Use the Sora CLI for local development dependencies:

### 1. Install Sora CLI
```bash
# If in the Sora repo
./scripts/cli-all.ps1

# Or download from releases
# Adds 'Sora.exe' to your PATH
```

### 2. Create Orchestration Descriptor
```yaml
# sora.orchestration.yml
dependencies:
  postgres:
    provider: docker
    image: postgres:15
    ports:
      - "5432:5432"
    environment:
      POSTGRES_DB: myapp
      POSTGRES_USER: user
      POSTGRES_PASSWORD: pass
    health:
      test: ["CMD-SHELL", "pg_isready -U user"]
      
  redis:
    provider: docker  
    image: redis:7-alpine
    ports:
      - "6379:6379"
    health:
      test: ["CMD", "redis-cli", "ping"]
```

### 3. Start Dependencies
```bash
# Export compose file and start services
Sora export compose --profile Local
Sora up --profile Local --timeout 300

# Check status
Sora status

# View logs
Sora logs

# Stop when done
Sora down
```

### 4. Update Configuration
```json
{
  "Sora": {
    "Data": {
      "DefaultProvider": "Postgres",
      "Postgres": {
        "ConnectionString": "Host=localhost;Database=myapp;Username=user;Password=pass"
      }
    },
    "Messaging": {
      "DefaultProvider": "RabbitMq",
      "RabbitMq": {
        "ConnectionString": "amqp://guest:guest@localhost:5672"
      }
    }
  }
}
```

---

## 🎯 Common Patterns

### Entity Relationships
```csharp
public class User : Entity<User>
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    
    // Foreign key
    public string UserId { get; set; } = "";
    
    // Navigation (resolved via query)
    public Task<User?> GetUser() => User.ByIdAsync(UserId);
    
    // Static queries with relationships
    public static Task<Todo[]> ForUser(string userId) =>
        All().Where(t => t.UserId == userId);
}
```

### Custom Business Logic
```csharp
public class Order : Entity<Order>
{
    public string CustomerEmail { get; set; } = "";
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    
    // Business methods
    public async Task MarkShipped()
    {
        Status = OrderStatus.Shipped;
        await SaveAsync();
        
        // Send notification
        await new OrderShippedEvent { OrderId = Id, CustomerEmail = CustomerEmail }.Send();
    }
    
    // Complex queries
    public static Task<Order[]> RecentOrders(TimeSpan timespan) =>
        All().Where(o => o.Created > DateTimeOffset.UtcNow - timespan)
             .OrderByDescending(o => o.Created);
}
```

### Background Services
```csharp
public class OrderProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Process order events
        await this.On<OrderShippedEvent>(async evt =>
        {
            // Send email notification
            await SendShippingNotification(evt.CustomerEmail, evt.OrderId);
        });
    }
}
```

---

## 📊 Health and Monitoring

Sora includes comprehensive health checks:

### Built-in Endpoints
- `GET /health` - Overall health
- `GET /health/live` - Liveness probe  
- `GET /health/ready` - Readiness probe

### Custom Health Checks
```csharp
public class ExternalApiHealthCheck : IHealthContributor
{
    public string Name => "external-api";
    public bool IsCritical => false;
    
    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", ct);
            return response.IsSuccessStatusCode 
                ? HealthReport.Healthy("External API is responding")
                : HealthReport.Unhealthy($"External API returned {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthReport.Unhealthy("External API is unreachable", ex);
        }
    }
}
```

---

## 🚨 Common Gotchas

### 1. Controller Routes
- ✅ Use `[Route("api/[controller]")]` 
- ❌ Don't use inline endpoints (`app.MapGet`)

### 2. Entity Methods
- ✅ Use static methods: `Todo.All()`, `Todo.Pending()`
- ❌ Don't use generic facades unless necessary: `Data<Todo>.All()`

### 3. Configuration
- ✅ Use `Sora:` prefix in appsettings.json
- ✅ Use environment variables: `SORA__DATA__DEFAULTPROVIDER`
- ❌ Don't hardcode connection strings

### 4. Dependencies
- ✅ Use orchestration descriptors for local dev
- ❌ Don't commit absolute paths or hardcoded URLs

---

## 📖 What's Next?

Now that you have a basic Sora application running:

1. **Explore Samples**: Check the `samples/` directory for real-world examples
2. **Read Usage Patterns**: See `REF_SORA_USAGE_PATTERNS.md`
3. **Deep Dive**: Explore pillar-specific documentation
4. **Join Community**: GitHub Discussions for questions

### Recommended Learning Path

1. **Start Here**: Get comfortable with Entity and Controller patterns
2. **Add Data**: Try different data providers (Postgres, MongoDB)
3. **Add AI**: Experiment with local LLMs and vector search
4. **Add Messaging**: Build event-driven features
5. **Production**: Learn about health checks, observability, deployment

---

**Welcome to Sora! Build services like you're talking to your code, not fighting it.**
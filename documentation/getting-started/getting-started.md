---
type: GUIDE
domain: core
title: "Complete Getting Started Guide: Mastering Koan Framework"
audience: [developers, technical-leaders, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Complete Getting Started Guide: Mastering Koan Framework

**From first project to sophisticated AI-native applications - a comprehensive journey.**

**Target Audience**: Developers ready to master modern .NET development patterns
**Framework Version**: v0.2.18+

---

## Framework Philosophy

Koan Framework embodies five core experiences that transform how you build .NET applications:

1. **✨ Try it, be delighted** - Immediate productivity and pleasant surprises
2. **🎯 Entity<> scales elegantly** - One pattern from CRUD to enterprise architecture
3. **🤖 AI feels native** - AI integration through familiar patterns
4. **⚡ Intelligent automation** - Small teams build sophisticated solutions
5. **🔧 Works with what you know** - Enhances your existing .NET workflow

This guide will take you through each experience systematically, building from simple concepts to sophisticated real-world applications.

---

## Prerequisites and Setup

### **System Requirements**
- **.NET 9 SDK** or later ([Download](https://dotnet.microsoft.com/download))
- **IDE**: Visual Studio 2022, VS Code, or JetBrains Rider
- **Docker** (optional, for local dependencies and container development)
- **Git** (for sample repositories and version control)

### **Recommended Knowledge**
- Basic C# and .NET development experience
- Familiarity with REST APIs and Entity Framework concepts
- Understanding of dependency injection and middleware patterns
- Basic SQL and NoSQL database concepts

### **Verify Installation**
```bash
# Check .NET version
dotnet --version
# Should return 9.0.0 or later

# Check Docker (if using containers)
docker --version
# Should return Docker version information
```

---

## Chapter 1: Your First Koan Application *(Try it, be delighted)*

### **The 2-Minute API**

Create a complete REST API with enterprise features in under 2 minutes:

```bash
# Create and navigate to project
mkdir TodoApp && cd TodoApp
dotnet new web

# Add Koan packages - Reference = Intent
dotnet add package Koan.Core
dotnet add package Koan.Web
dotnet add package Koan.Data.Sqlite
```

### **Define Your Domain Model**

```csharp
// Models/Todo.cs
using Koan.Data.Abstractions;

namespace TodoApp.Models;

public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsCompleted { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public string Tags { get; set; } = "";
}

public enum Priority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
}
```

### **Create the API Controller**

```csharp
// Controllers/TodosController.cs
using Microsoft.AspNetCore.Mvc;
using Koan.Web;
using TodoApp.Models;

namespace TodoApp.Controllers;

[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    // EntityController<T> provides:
    // - GET /api/todos (with paging, filtering)
    // - GET /api/todos/{id}
    // - POST /api/todos
    // - PUT /api/todos/{id}
    // - DELETE /api/todos/{id}
    // - Full validation and error handling
    // - Health check integration
    // - Structured logging
}
```

### **Configure the Application**

```csharp
// Program.cs
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);

// Add Koan services - auto-discovers all referenced packages
builder.Services.AddKoan();

var app = builder.Build();

// Koan.Web automatically configures:
// - Controllers and routing
// - Health checks
// - Error handling
// - CORS (development)
// - Structured logging
// - OpenAPI/Swagger (development)

app.Run();
```

### **Run and Test**

```bash
# Start the application
dotnet run

# Application automatically provides:
# ✅ REST API at http://localhost:5000/api/todos
# ✅ Health checks at http://localhost:5000/api/health
# ✅ Swagger UI at http://localhost:5000/swagger (development)
# ✅ Auto-generated GUID v7 IDs
# ✅ SQLite database with zero configuration
# ✅ Structured logging and telemetry
```

### **Test the API**

```bash
# Create a todo with auto-generated ID
curl -X POST http://localhost:5000/api/todos \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Learn Koan Framework",
    "description": "Complete the getting started guide",
    "priority": 2,
    "dueDate": "2024-12-31T00:00:00Z",
    "tags": "learning,framework"
  }'

# Get all todos (note the auto-generated GUID v7 ID)
curl http://localhost:5000/api/todos

# Get a specific todo
curl http://localhost:5000/api/todos/{generated-id}

# Update a todo
curl -X PUT http://localhost:5000/api/todos/{generated-id} \
  -H "Content-Type: application/json" \
  -d '{"title": "Updated Todo", "isCompleted": true}'

# Check application health
curl http://localhost:5000/api/health
```

### **What You Just Experienced**

**Pleasant Surprises:**
- **Zero configuration database** - SQLite automatically configured and initialized
- **Auto-generated IDs** - GUID v7 timestamps for natural ordering
- **Full validation** - Model validation and error responses automatically handled
- **Health monitoring** - Built-in health checks for dependencies
- **Structured logging** - Rich telemetry with correlation IDs
- **Development tools** - Swagger UI and detailed error pages

**This is the "Try it, be delighted" experience - sophisticated features through simple patterns.**

---

## Chapter 2: Pattern Scaling *(Entity<> scales elegantly)*

### **Understanding the Entity<> Pattern**

The `Entity<T>` pattern is the heart of Koan Framework. It provides:

- **Consistent API** across all domains (data, messaging, AI)
- **Auto-generated primary keys** (GUID v7 by default)
- **Provider transparency** - same code, different backends
- **Built-in optimizations** - query capabilities, caching, telemetry

### **Extending to Messaging**

Add event-driven capabilities with the same pattern:

```csharp
// Models/TodoCompleted.cs
public class TodoCompleted : Entity<TodoCompleted>
{
    public string TodoId { get; set; } = "";
    public string Title { get; set; } = "";
    public string CompletedBy { get; set; } = "";
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

// Models/TodoAssigned.cs
public class TodoAssigned : Entity<TodoAssigned>
{
    public string TodoId { get; set; } = "";
    public string AssignedTo { get; set; } = "";
    public string AssignedBy { get; set; } = "";
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
```

### **Add Business Logic**

```csharp
// Controllers/TodosController.cs
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    public override async Task<ActionResult<Todo>> Put(string id, Todo todo)
    {
        var existing = await Todo.Get(id);
        if (existing == null) return NotFound();

        // Call base implementation for standard update
        var result = await base.Put(id, todo);

        // Business logic using same Entity<> pattern
        if (todo.IsCompleted && !existing.IsCompleted)
        {
            // Send completion event - same .Send() pattern as .Save()
            await new TodoCompleted {
                TodoId = todo.Id,
                Title = todo.Title,
                CompletedBy = User.Identity?.Name ?? "Anonymous"
            }.Send();
        }

        return result;
    }

    [HttpPost("{id}/assign")]
    public async Task<ActionResult> AssignTodo(string id, [FromBody] AssignmentRequest request)
    {
        var todo = await Todo.Get(id);
        if (todo == null) return NotFound();

        // Send assignment event
        await new TodoAssigned {
            TodoId = id,
            AssignedTo = request.AssignedTo,
            AssignedBy = User.Identity?.Name ?? "Anonymous"
        }.Send();

        return Ok();
    }
}

// DTOs/AssignmentRequest.cs
public class AssignmentRequest
{
    public string AssignedTo { get; set; } = "";
}
```

### **Enable Messaging**

```bash
# Reference = Intent - add messaging capability
dotnet add package Koan.Messaging.InMemory

# For production, use:
# dotnet add package Koan.Messaging.RabbitMq
```

### **Handle Events**

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

// Event handlers using same Entity<> patterns
Flow.OnCreate<TodoCompleted>(async (completed) => {
    Console.WriteLine($"Todo '{completed.Title}' completed by {completed.CompletedBy}");

    // Could trigger notifications, updates, etc.
    // All using the same Entity<> patterns
    return UpdateResult.Continue();
});

Flow.OnCreate<TodoAssigned>(async (assignment) => {
    Console.WriteLine($"Todo {assignment.TodoId} assigned to {assignment.AssignedTo}");
    return UpdateResult.Continue();
});

var app = builder.Build();
app.Run();
```

### **Test Event-Driven Behavior**

```bash
# Complete a todo - triggers TodoCompleted event
curl -X PUT http://localhost:5000/api/todos/{id} \
  -H "Content-Type: application/json" \
  -d '{"title": "Learn Events", "isCompleted": true}'

# Assign a todo - triggers TodoAssigned event
curl -X POST http://localhost:5000/api/todos/{id}/assign \
  -H "Content-Type: application/json" \
  -d '{"assignedTo": "john@company.com"}'

# Check console output for event handling logs
```

### **Pattern Consistency Demonstrated**

```csharp
// Same pattern across domains:

// Database operations
await todo.Save();           // Store entity
var todo = await Todo.Get(id);  // Retrieve entity

// Messaging operations
await event.Send();          // Send event entity
// Events are automatically routed and handled

// The mental model remains consistent:
// Entity<> provides .Save(), .Get(), .Send() - same pattern, different capabilities
```

---

## Chapter 3: AI Integration *(AI feels native)*

### **Adding AI Capabilities**

Transform your Todo app into an AI-native application:

```bash
# Reference = Intent - add AI capability
dotnet add package Koan.AI.Ollama

# For production, also consider:
# dotnet add package Koan.Data.Vector  (for semantic search)
# dotnet add package Koan.AI.OpenAI    (for cloud AI)
```

### **AI-Enhanced Controllers**

```csharp
// Controllers/TodosController.cs
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    private readonly IAiService _ai;

    public TodosController(IAiService ai)
    {
        _ai = ai;
    }

    [HttpPost("{id}/suggestions")]
    public async Task<ActionResult<string[]>> GetSuggestions(string id)
    {
        var todo = await Todo.Get(id);
        if (todo == null) return NotFound();

        // AI integration feels like any other service call
        var prompt = $@"
            Based on this todo: '{todo.Title}' - {todo.Description}
            Priority: {todo.Priority}
            Tags: {todo.Tags}

            Suggest 3 specific next steps or related tasks.
            Return as a JSON array of strings.
        ";

        var response = await _ai.Chat(prompt);
        return Ok(response);
    }

    [HttpPost("generate")]
    public async Task<ActionResult<Todo>> GenerateTodo([FromBody] TodoGenerationRequest request)
    {
        var prompt = $@"
            Generate a well-structured todo item for: {request.Description}
            Context: {request.Context}

            Provide:
            - Clear, actionable title
            - Detailed description
            - Appropriate priority (1=Low, 2=Normal, 3=High, 4=Critical)
            - Relevant tags (comma-separated)
            - Estimated due date

            Return as JSON matching the Todo model structure.
        ";

        var aiResponse = await _ai.Chat(prompt);

        // Parse AI response into Todo entity (simplified for demo)
        var generatedTodo = System.Text.Json.JsonSerializer.Deserialize<Todo>(aiResponse);

        // Save using standard Entity<> pattern
        await generatedTodo.Save();

        return Ok(generatedTodo);
    }

    [HttpGet("semantic-search")]
    public async Task<ActionResult<IEnumerable<Todo>>> SemanticSearch([FromQuery] string query)
    {
        // Semantic search through familiar Entity<> patterns
        // This requires Koan.Data.Vector package
        var results = await Todo.SemanticSearch(query);
        return Ok(results);
    }

    [HttpPost("{id}/smart-complete")]
    public async Task<ActionResult<Todo>> SmartComplete(string id)
    {
        var todo = await Todo.Get(id);
        if (todo == null) return NotFound();

        // AI-powered completion analysis
        var completionAnalysis = await _ai.Chat($@"
            Analyze this completed todo: '{todo.Title}'
            Description: {todo.Description}

            Generate:
            1. Completion summary
            2. Suggested follow-up tasks
            3. Lessons learned or insights

            Return as structured JSON.
        ");

        // Mark complete and send enhanced event
        todo.IsCompleted = true;
        await todo.Save();

        await new TodoCompleted {
            TodoId = todo.Id,
            Title = todo.Title,
            CompletedBy = User.Identity?.Name ?? "AI Assistant",
            CompletionAnalysis = completionAnalysis
        }.Send();

        return Ok(todo);
    }
}

// DTOs/TodoGenerationRequest.cs
public class TodoGenerationRequest
{
    public string Description { get; set; } = "";
    public string Context { get; set; } = "";
}
```

### **Enhanced Event Models**

```csharp
// Models/TodoCompleted.cs - Enhanced with AI insights
public class TodoCompleted : Entity<TodoCompleted>
{
    public string TodoId { get; set; } = "";
    public string Title { get; set; } = "";
    public string CompletedBy { get; set; } = "";
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public string CompletionAnalysis { get; set; } = ""; // AI-generated insights
}
```

### **AI-Driven Event Handlers**

```csharp
// Program.cs - AI-enhanced event processing
Flow.OnCreate<TodoCompleted>(async (completed) => {
    // AI analyzes completion patterns
    var analysis = await ai.Chat($@"
        A todo titled '{completed.Title}' was completed.
        Analysis: {completed.CompletionAnalysis}

        Based on this completion:
        1. Should we suggest related todos?
        2. Are there recurring patterns?
        3. What productivity insights can we extract?

        Return actionable insights.
    ");

    // Generate follow-up suggestions automatically
    var suggestions = await ai.Chat($@"
        Based on completing '{completed.Title}', suggest 2-3 related todos that might be valuable.
        Return as JSON array of todo objects.
    ");

    Console.WriteLine($"AI Insights: {analysis}");
    Console.WriteLine($"AI Suggestions: {suggestions}");

    return UpdateResult.Continue();
});
```

### **Test AI Features**

```bash
# Generate AI-powered todo
curl -X POST http://localhost:5000/api/todos/generate \
  -H "Content-Type: application/json" \
  -d '{
    "description": "Prepare for team presentation",
    "context": "Software development team, quarterly review"
  }'

# Get AI suggestions for existing todo
curl -X POST http://localhost:5000/api/todos/{id}/suggestions

# Semantic search (finds todos by meaning, not just keywords)
curl "http://localhost:5000/api/todos/semantic-search?query=team collaboration"

# AI-powered completion with insights
curl -X POST http://localhost:5000/api/todos/{id}/smart-complete
```

### **AI Configuration Options**

```json
// appsettings.json - AI provider configuration
{
  "Koan": {
    "AI": {
      "Provider": "Ollama",
      "Ollama": {
        "BaseUrl": "http://localhost:11434",
        "DefaultModel": "llama2",
        "Temperature": 0.7
      },
      "OpenAI": {
        "ApiKey": "your-api-key",
        "DefaultModel": "gpt-4",
        "Temperature": 0.7
      }
    }
  }
}
```

---

## Chapter 4: Intelligent Automation *(Small teams, sophisticated solutions)*

### **Multi-Provider Data Scaling**

Scale your application across different data providers without code changes:

```bash
# Development: SQLite (automatic)
# Already configured

# Staging: Add PostgreSQL
dotnet add package Koan.Data.Postgres

# Production: Add Redis for caching and Vector for AI
dotnet add package Koan.Data.Redis
dotnet add package Koan.Data.Vector
```

### **Provider-Specific Configurations**

```json
// appsettings.Development.json
{
  "ConnectionStrings": {
    "Default": "Data Source=todos.db"  // SQLite
  }
}

// appsettings.Staging.json
{
  "ConnectionStrings": {
    "Default": "Host=staging-db;Database=todos;Username=app;Password=***"
  }
}

// appsettings.Production.json
{
  "ConnectionStrings": {
    "Default": "Host=prod-db;Database=todos;Username=app;Password=***",
    "Cache": "localhost:6379",  // Redis
    "Vector": "http://weaviate:8080"  // Vector DB
  }
}
```

### **Advanced Entity Patterns**

```csharp
// Models/Todo.cs - Advanced entity with multi-provider awareness
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsCompleted { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public string Tags { get; set; } = "";

    // Vector search support (when Koan.Data.Vector is referenced)
    [VectorSearchable]
    public string SearchableContent => $"{Title} {Description} {Tags}";

    // Caching support (when Koan.Data.Redis is referenced)
    [Cacheable(ExpirationMinutes = 30)]
    public bool IsCacheEnabled => Priority >= Priority.High;
}

// Models/TodoAnalytics.cs - Separate concern with different storage
[SourceAdapter("analytics-mongo")]  // Force specific provider
public class TodoAnalytics : Entity<TodoAnalytics>
{
    public string TodoId { get; set; } = "";
    public string Action { get; set; } = "";
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
```

### **Sophisticated Event-Driven Architecture**

```csharp
// Program.cs - Enterprise event-driven patterns
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

// Multi-stage event processing pipeline
Flow.OnUpdate<Todo>(async (todo, previous) => {
    var changes = new List<string>();

    // Detect changes
    if (todo.Priority != previous.Priority)
        changes.Add($"Priority: {previous.Priority} → {todo.Priority}");

    if (todo.IsCompleted != previous.IsCompleted)
        changes.Add($"Completed: {previous.IsCompleted} → {todo.IsCompleted}");

    // Generate analytics event
    await new TodoAnalytics {
        TodoId = todo.Id,
        Action = "Updated",
        Metadata = new Dictionary<string, object> {
            ["Changes"] = changes,
            ["UserId"] = "current-user", // Would come from context
            ["SessionId"] = Guid.NewGuid().ToString()
        }
    }.Save();

    // AI-powered change analysis
    if (changes.Any())
    {
        var ai = builder.Services.BuildServiceProvider().GetService<IAiService>();
        var analysis = await ai.Chat($@"
            Todo '{todo.Title}' was updated:
            Changes: {string.Join(", ", changes)}

            Analyze:
            1. Is this a significant change?
            2. Should team members be notified?
            3. Are there process improvements to suggest?

            Return structured recommendations.
        ");

        await new TodoChangeAnalyzed {
            TodoId = todo.Id,
            Changes = changes,
            AIAnalysis = analysis,
            Timestamp = DateTime.UtcNow
        }.Send();
    }

    return UpdateResult.Continue();
});

// Completion workflow automation
Flow.OnCreate<TodoCompleted>(async (completed) => {
    // Multi-step automated workflow

    // 1. Update completion metrics
    await new TodoAnalytics {
        TodoId = completed.TodoId,
        Action = "Completed",
        Metadata = new Dictionary<string, object> {
            ["CompletedBy"] = completed.CompletedBy,
            ["CompletionTime"] = completed.CompletedAt,
            ["Duration"] = "calculated-from-creation" // Would be calculated
        }
    }.Save();

    // 2. Generate AI insights for future planning
    var ai = builder.Services.BuildServiceProvider().GetService<IAiService>();
    var insights = await ai.Chat($@"
        Todo '{completed.Title}' was completed by {completed.CompletedBy}.

        Generate:
        1. Productivity insights
        2. Pattern recognition
        3. Suggestions for similar future tasks

        Focus on actionable recommendations.
    ");

    // 3. Create follow-up recommendations
    var recommendations = await ai.Chat($@"
        Based on completing '{completed.Title}', what related tasks might be valuable?
        Consider:
        - Dependencies that might now be unblocked
        - Follow-up actions typically needed
        - Related work that builds on this completion

        Return 2-3 specific todo recommendations as JSON array.
    ");

    await new CompletionInsights {
        TodoId = completed.TodoId,
        Insights = insights,
        Recommendations = recommendations,
        GeneratedAt = DateTime.UtcNow
    }.Send();

    return UpdateResult.Continue();
});

var app = builder.Build();
app.Run();
```

### **Container Orchestration Generation**

Generate sophisticated deployment configurations automatically:

```bash
# Generate Docker Compose for different environments
koan export compose --profile Local --output local-compose.yml
koan export compose --profile Staging --output staging-compose.yml
koan export compose --profile Production --output production-compose.yml

# Generated files include:
# ✅ Service definitions with proper dependencies
# ✅ Health check configurations
# ✅ Environment-specific networking
# ✅ Volume configurations for data persistence
# ✅ Resource limits and scaling policies
```

Example generated `production-compose.yml`:
```yaml
version: '3.8'
services:
  todoapp:
    image: todoapp:latest
    ports:
      - "8080:80"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__Default=Host=postgres;Database=todos;Username=app;Password=${DB_PASSWORD}
      - ConnectionStrings__Cache=redis:6379
      - ConnectionStrings__Vector=http://weaviate:8080
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
      weaviate:
        condition: service_healthy
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost/api/health"]
      interval: 30s
      timeout: 10s
      retries: 3

  postgres:
    image: postgres:15
    environment:
      POSTGRES_DB: todos
      POSTGRES_USER: app
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U app -d todos"]
      interval: 10s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7-alpine
    volumes:
      - redis_data:/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 3s
      retries: 3

  weaviate:
    image: semitechnologies/weaviate:latest
    ports:
      - "8081:8080"
    environment:
      QUERY_DEFAULTS_LIMIT: 25
      AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED: 'true'
    volumes:
      - weaviate_data:/var/lib/weaviate

volumes:
  postgres_data:
  redis_data:
  weaviate_data:
```

### **Monitoring and Observability**

```csharp
// Program.cs - Built-in enterprise observability
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan(options => {
    options.EnableDetailedTelemetry = true;
    options.HealthCheckTimeout = TimeSpan.FromSeconds(30);
    options.MetricsExportInterval = TimeSpan.FromMinutes(1);
});

// Custom health contributors
builder.Services.AddSingleton<IHealthContributor, CustomHealthContributor>();

var app = builder.Build();

// Built-in endpoints automatically available:
// /api/health - Comprehensive health status
// /api/health/live - Kubernetes liveness probe
// /api/health/ready - Kubernetes readiness probe
// /metrics - Prometheus metrics (if enabled)

app.Run();

// Custom health monitoring
public class CustomHealthContributor : IHealthContributor
{
    private readonly IAiService _ai;

    public CustomHealthContributor(IAiService ai) => _ai = ai;

    public async Task<HealthReport> CheckAsync(CancellationToken ct)
    {
        try
        {
            // Test AI service connectivity
            await _ai.Chat("Health check test", ct);
            return HealthReport.Healthy("AI service operational");
        }
        catch (Exception ex)
        {
            return HealthReport.Unhealthy("AI service unavailable", ex);
        }
    }
}
```

---

## Chapter 5: Ecosystem Integration *(Works with what you know)*

### **ASP.NET Core Integration**

Koan works seamlessly with existing ASP.NET Core patterns:

```csharp
// Program.cs - Standard ASP.NET Core with Koan enhancements
var builder = WebApplication.CreateBuilder(args);

// Standard ASP.NET Core services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Koan services (discovers and integrates automatically)
builder.Services.AddKoan();

// Custom services work alongside Koan
builder.Services.AddScoped<ICustomService, CustomService>();
builder.Services.AddSingleton<IEmailService, EmailService>();

var app = builder.Build();

// Standard ASP.NET Core middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

// Koan controllers work alongside standard controllers
app.MapControllers();

// Custom minimal APIs alongside EntityController<T>
app.MapGet("/api/status", () => new {
    Status = "Running",
    Timestamp = DateTime.UtcNow,
    Version = "1.0.0"
});

app.Run();
```

### **Custom Controllers with Koan Services**

```csharp
// Controllers/CustomController.cs - Standard controller using Koan services
[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IAiService _ai;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(IAiService ai, ILogger<ReportsController> logger)
    {
        _ai = ai;
        _logger = logger;
    }

    [HttpGet("productivity")]
    public async Task<ActionResult<ProductivityReport>> GetProductivityReport([FromQuery] DateTime? since)
    {
        var startDate = since ?? DateTime.UtcNow.AddDays(-30);

        // Use standard Entity<> queries alongside custom logic
        var completedTodos = await Todo.Query(t =>
            t.IsCompleted &&
            t.CreatedAt >= startDate
        );

        var analytics = await TodoAnalytics.Query(a =>
            a.Action == "Completed" &&
            a.Timestamp >= startDate
        );

        // AI analysis using Koan AI services
        var aiInsights = await _ai.Chat($@"
            Analyze productivity data:
            - {completedTodos.Count} todos completed
            - Period: {startDate:yyyy-MM-dd} to {DateTime.UtcNow:yyyy-MM-dd}
            - Analytics events: {analytics.Count}

            Provide productivity insights and recommendations.
        ");

        var report = new ProductivityReport
        {
            Period = new DateRange(startDate, DateTime.UtcNow),
            CompletedTasks = completedTodos.Count,
            Insights = aiInsights,
            GeneratedAt = DateTime.UtcNow
        };

        return Ok(report);
    }
}

// DTOs/ProductivityReport.cs
public class ProductivityReport
{
    public DateRange Period { get; set; }
    public int CompletedTasks { get; set; }
    public string Insights { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
}

public class DateRange
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    public DateRange(DateTime start, DateTime end)
    {
        Start = start;
        End = end;
    }
}
```

### **Docker Integration**

```dockerfile
# Dockerfile - Standard .NET containerization
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["TodoApp.csproj", "."]
RUN dotnet restore "TodoApp.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "TodoApp.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TodoApp.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Koan applications work with standard container practices
ENV ASPNETCORE_URLS=http://+:80
ENTRYPOINT ["dotnet", "TodoApp.dll"]
```

### **Testing Integration**

```csharp
// Tests/TodosControllerTests.cs - Standard testing with Koan
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class TodosControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TodosControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_ReturnsSuccessStatusCode()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/todos");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Post_CreatesTodo_ReturnsCreatedTodo()
    {
        // Arrange
        var client = _factory.CreateClient();
        var todo = new {
            title = "Test Todo",
            description = "Test Description",
            priority = 2
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/todos", todo);

        // Assert
        response.EnsureSuccessStatusCode();
        var createdTodo = await response.Content.ReadFromJsonAsync<Todo>();
        Assert.NotNull(createdTodo);
        Assert.Equal("Test Todo", createdTodo.Title);
        Assert.NotEqual(Guid.Empty.ToString(), createdTodo.Id);
    }

    [Fact]
    public async Task AiService_Integration_Works()
    {
        // Test AI service integration
        using var scope = _factory.Services.CreateScope();
        var ai = scope.ServiceProvider.GetRequiredService<IAiService>();

        var response = await ai.Chat("Generate a simple test message");

        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }
}
```

### **CI/CD Integration**

```yaml
# .github/workflows/build-and-test.yml
name: Build and Test

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '9.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore

    - name: Test
      run: dotnet test --no-build --verbosity normal

    - name: Generate Docker Compose
      run: |
        dotnet run --project TodoApp -- export compose --profile Production

    - name: Build Docker image
      run: docker build -t todoapp:latest .

    - name: Test Docker image
      run: |
        docker run -d --name test-app -p 8080:80 todoapp:latest
        sleep 10
        curl -f http://localhost:8080/api/health || exit 1
        docker stop test-app
```

---

## Advanced Patterns and Best Practices

### **Performance Optimization**

```csharp
// Optimized entity queries
public class OptimizedTodosController : EntityController<Todo>
{
    [HttpGet("high-priority")]
    public async Task<ActionResult<IEnumerable<Todo>>> GetHighPriorityTodos()
    {
        // Leverages provider-specific optimizations
        var todos = await Todo.Query(t =>
            t.Priority >= Priority.High &&
            !t.IsCompleted
        );

        return Ok(todos);
    }

    [HttpGet("analytics")]
    public async Task<ActionResult<object>> GetAnalytics()
    {
        // Parallel queries for better performance
        var tasks = new[]
        {
            Todo.Count(t => t.IsCompleted),
            Todo.Count(t => !t.IsCompleted),
            Todo.Count(t => t.Priority == Priority.Critical),
            TodoAnalytics.Count(a => a.Action == "Completed" && a.Timestamp >= DateTime.UtcNow.AddDays(-7))
        };

        var results = await Task.WhenAll(tasks);

        return Ok(new {
            CompletedCount = results[0],
            PendingCount = results[1],
            CriticalCount = results[2],
            WeeklyCompletions = results[3]
        });
    }
}
```

### **Security Best Practices**

```csharp
// Secure controller with authorization
[ApiController]
[Route("api/[controller]")]
[Authorize] // Standard ASP.NET Core authorization
public class SecureTodosController : EntityController<Todo>
{
    public override async Task<ActionResult<Todo>> Post(Todo todo)
    {
        // Add user context to entities
        todo.CreatedBy = User.Identity?.Name ?? "Anonymous";
        todo.TenantId = GetCurrentTenantId(); // Multi-tenant support

        return await base.Post(todo);
    }

    public override async Task<ActionResult<IEnumerable<Todo>>> Get([FromQuery] DataQueryOptions? options = null)
    {
        // Apply tenant filtering automatically
        var tenantId = GetCurrentTenantId();
        var todos = await Todo.Query(t => t.TenantId == tenantId);

        return Ok(todos);
    }

    private string GetCurrentTenantId()
    {
        return User.FindFirst("tenant_id")?.Value ?? "default";
    }
}

// Enhanced Todo model with security
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsCompleted { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public string Tags { get; set; } = "";

    // Security and auditing fields
    public string CreatedBy { get; set; } = "";
    public string TenantId { get; set; } = "";
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public string LastModifiedBy { get; set; } = "";
}
```

---

## Deployment and Production Considerations

### **Environment Configuration**

```json
// appsettings.Production.json - Production-ready configuration
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Koan": "Information"
    }
  },
  "Koan": {
    "Data": {
      "DefaultProvider": "postgres",
      "ConnectionTimeout": 30,
      "CommandTimeout": 120,
      "MaxRetries": 3
    },
    "AI": {
      "Provider": "OpenAI",
      "OpenAI": {
        "ApiKey": "${OPENAI_API_KEY}",
        "DefaultModel": "gpt-4",
        "Temperature": 0.7,
        "MaxTokens": 2000
      }
    },
    "Messaging": {
      "Provider": "RabbitMQ",
      "RabbitMQ": {
        "ConnectionString": "${RABBITMQ_CONNECTION}",
        "RetryAttempts": 3,
        "RetryDelay": "00:00:05"
      }
    },
    "Health": {
      "TimeoutSeconds": 30,
      "EnableDetailedErrors": false
    }
  },
  "ConnectionStrings": {
    "Default": "${DATABASE_CONNECTION}",
    "Cache": "${REDIS_CONNECTION}",
    "Vector": "${VECTOR_DB_CONNECTION}"
  }
}
```

### **Production Monitoring**

```csharp
// Program.cs - Production monitoring configuration
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan(options => {
    // Production telemetry
    options.EnableDetailedTelemetry = builder.Environment.IsProduction();
    options.EnableMetrics = true;
    options.MetricsExportInterval = TimeSpan.FromMinutes(1);

    // Health check configuration
    options.HealthCheckTimeout = TimeSpan.FromSeconds(30);
    options.EnableHealthCheckUI = true;

    // Performance optimization
    options.EnableQueryOptimization = true;
    options.EnableCaching = true;
    options.CacheDuration = TimeSpan.FromMinutes(15);
});

// Add production health checks
builder.Services.AddHealthChecks()
    .AddCheck("database", () => HealthCheckResult.Healthy())
    .AddCheck("external-api", () => HealthCheckResult.Healthy());

var app = builder.Build();

// Production middleware pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
```

---

## Next Steps and Resources

### **Continue Your Journey**

**For Individual Developers:**
- **[Building Advanced APIs](../guides/building-apis.md)** - REST, GraphQL, real-time patterns
- **[AI Integration Patterns](../guides/ai-integration.md)** - Chat, embeddings, semantic search
- **[Performance Optimization](../guides/performance.md)** - Query optimization, caching, monitoring

**For Teams and Architects:**
- **[Enterprise Architecture Guide](enterprise-adoption.md)** - Strategic framework adoption
- **[Container Orchestration](../guides/orchestration.md)** - Docker, Kubernetes, Aspire
- **[Security Best Practices](../guides/security.md)** - Authentication, authorization, compliance

**For AI-First Development:**
- **[MCP Integration](../guides/mcp-http-sse-howto.md)** - AI-assisted development workflow
- **[Vector Search Implementation](../guides/vector-search.md)** - Semantic search and embeddings
- **[Event-Driven AI](../guides/ai-event-patterns.md)** - AI in event-driven architectures

### **Sample Applications**

Explore real-world implementations in the [samples directory](../../samples/):

- **S1.Hello** - Basic Koan application patterns
- **S8.Location** - Multi-provider location services with AI
- **S12.MedTrials** - Enterprise healthcare application with MCP integration
- **AI-Chat-Demo** - Conversational interfaces and semantic search
- **Event-Driven-Commerce** - E-commerce with sophisticated event patterns

### **Community and Support**

- **[Framework Documentation](../README.md)** - Complete reference and guides
- **[GitHub Repository](https://github.com/koan-framework)** - Source code and issue tracking
- **[Community Discord](https://discord.gg/koan-framework)** - Real-time discussion and support
- **[Stack Overflow](https://stackoverflow.com/questions/tagged/koan-framework)** - Q&A and troubleshooting

---

## Summary: Your Koan Framework Journey

**You've experienced the complete Koan Framework transformation:**

### **✨ Chapter 1: Try it, be delighted**
- Created sophisticated REST API in 2 minutes
- Experienced zero-configuration productivity
- Discovered pleasant surprises (health checks, telemetry, GUID v7)

### **🎯 Chapter 2: Entity<> scales elegantly**
- Extended same pattern to messaging and events
- Learned Reference = Intent dependency management
- Built event-driven architecture with familiar APIs

### **🤖 Chapter 3: AI feels native**
- Integrated AI through standard dependency injection
- Added semantic search and chat capabilities
- Enhanced business logic with AI insights

### **⚡ Chapter 4: Intelligent automation**
- Scaled across multiple data providers seamlessly
- Generated sophisticated deployment artifacts automatically
- Implemented enterprise observability and monitoring

### **🔧 Chapter 5: Works with what you know**
- Integrated with standard ASP.NET Core patterns
- Used familiar tooling (Docker, testing, CI/CD)
- Enhanced existing workflows without replacement

**The Result:** You can now build sophisticated, AI-native, event-driven applications using simple, consistent patterns that scale from prototype to enterprise production.

**Welcome to the future of .NET development. Welcome to Koan.**

---

**Last Updated**: 2025-01-17 by Framework Development Team
**Framework Version Tested**: v0.2.18+
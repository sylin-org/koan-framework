# Koan MCP Integration Proposal

**Status:** Draft
**Author:** Koan Framework Team
**Date:** January 2025
**Version:** 1.0

## Abstract

This proposal outlines the design and implementation of `Koan.Mcp` - a module that automatically transforms Koan web APIs into Model Context Protocol (MCP) servers. Following Koan's "Reference = Intent" philosophy, developers can expose existing `EntityController<T>` APIs to Large Language Models (LLMs) with minimal code changes.

**Key Design Principles:**
- **DRY (Don't Repeat Yourself)**: Leverages existing `AssemblyCache` and reflection infrastructure from Koan.Core
- **KISS (Keep It Simple, Stupid)**: Reuses established discovery patterns instead of creating new reflection code
- **Clean Architecture**: `[Comment]` attributes on base class + `[McpController]` opt-in maintains separation of concerns

## Background

### What is MCP?

The Model Context Protocol (MCP) is an open standard introduced by Anthropic that enables standardized communication between Large Language Models and external data sources, tools, and services. MCP follows a client-server architecture where:

- **Servers** expose Tools, Resources, and Prompts via JSON-RPC 2.0
- **Clients** discover and execute tools on behalf of LLMs
- **Tools** are functions that LLMs can call to perform specific actions

### Problem Statement

Currently, exposing Koan APIs to LLMs requires:
1. Manual MCP server implementation
2. Duplicating business logic
3. Custom parameter mapping and validation
4. Separate deployment and maintenance

This violates Koan's principle of eliminating boilerplate and configuration complexity.

### Goals

1. **Zero Configuration**: Add package reference, get MCP functionality
2. **Clean Architecture**: No coupling between EntityController and MCP concerns
3. **Rich Tool Descriptions**: Meaningful metadata for LLM understanding
4. **Template System**: Consistent descriptions across entity types
5. **Progressive Enhancement**: Support custom business logic alongside CRUD operations

## Proposed Solution

### Core Architecture

The solution uses a **comment-attribute approach** that maintains separation of concerns:

```csharp
// EntityController base class (in Koan.Web)
public abstract class EntityController<TEntity, TKey> : ControllerBase
{
    [Comment("List all <TEntity> items with optional filtering and pagination")]
    [HttpGet("")]
    public virtual async Task<IActionResult> GetCollection(CancellationToken ct) { ... }

    [Comment("Retrieve a specific <TEntity> by its unique identifier")]
    [HttpGet("{id}")]
    public virtual async Task<IActionResult> GetById([FromRoute] TKey id, CancellationToken ct) { ... }

    [Comment("Create a new <TEntity> or update an existing one")]
    [HttpPost("")]
    public virtual async Task<IActionResult> Upsert([FromBody] TEntity model, CancellationToken ct) { ... }

    [Comment("Delete a <TEntity> by its identifier")]
    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete([FromRoute] TKey id, CancellationToken ct) { ... }
}

// Developer implementation (minimal)
[McpController]
public class TodosController : EntityController<Todo> { }

// Custom methods when needed
[McpController]
public class ProjectsController : EntityController<Project>
{
    [Comment("Generate AI-powered risk analysis for a <TEntity>")]
    [HttpPost("{id}/analyze-risks")]
    public async Task<IActionResult> AnalyzeRisks(string id) { ... }
}
```

### Template Processing

The `<TEntity>` placeholder in comment attributes gets replaced with actual type names:

- `"List all <TEntity> items"` → `"List all Todo items"` (TodosController)
- `"List all <TEntity> items"` → `"List all Project items"` (ProjectsController)

## Implementation Details

### Module Structure

```
src/Koan.Mcp/
├── Abstractions/
│   ├── IMcpServer.cs                 # Server interface
│   └── McpToolDefinition.cs          # Tool metadata
├── Attributes/
│   └── McpControllerAttribute.cs     # Opt-in controller decoration
├── Infrastructure/
│   ├── McpServer.cs                  # Core MCP server implementation
│   ├── StdioTransport.cs             # STDIO transport for local use
│   └── HttpSseTransport.cs           # HTTP+SSE transport for remote
├── Discovery/
│   ├── McpControllerDiscovery.cs     # Leverages AssemblyCache for discovery
│   ├── McpToolGenerator.cs           # Generate MCP tools from controllers
│   └── TemplateProcessor.cs          # Process comment templates
├── Execution/
│   ├── ToolExecutor.cs               # Execute controller methods as tools
│   └── ParameterConverter.cs         # Convert JSON args to method params
├── Initialization/
│   └── KoanAutoRegistrar.cs          # Follows existing auto-registration patterns
└── Options/
    └── McpServerOptions.cs           # Server configuration
```

**Key Simplifications:**
- **Leverages AssemblyCache**: Uses existing `AssemblyCache.Instance` instead of custom assembly scanning
- **Follows Established Patterns**: Discovery and registration follow patterns from `KoanBackgroundServiceAutoRegistrar`
- **Reuses Safe Type Loading**: Uses proven `GetTypesFromAssembly` error handling pattern
- **Minimal Reflection Code**: Most reflection infrastructure already exists in Koan.Core

### Key Components

#### 1. Controller Discovery (Leveraging Existing Infrastructure)

```csharp
public class McpControllerDiscovery
{
    public IEnumerable<Type> DiscoverMcpControllers()
    {
        // Use existing AssemblyCache instead of AppDomain.GetAssemblies()
        return AssemblyCache.Instance.GetAllAssemblies()
            .SelectMany(a => GetTypesFromAssembly(a))
            .Where(t => t.HasAttribute<McpControllerAttribute>())
            .Where(t => IsEntityController(t));
    }

    private bool IsEntityController(Type type)
    {
        return type.BaseType?.IsGenericType == true &&
               type.BaseType.GetGenericTypeDefinition() == typeof(EntityController<,>);
    }

    // Reuse existing pattern from KoanBackgroundServiceAutoRegistrar
    private static Type[] GetTypesFromAssembly(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Handle partial load - return types that loaded successfully
            return ex.Types.Where(t => t != null).Cast<Type>().ToArray();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}
```

#### 2. Tool Generation

```csharp
public class McpToolGenerator
{
    public IEnumerable<McpToolDefinition> GenerateFromController(Type controllerType)
    {
        var entityType = GetEntityType(controllerType);
        var prefix = entityType.Name.ToLowerInvariant() + "_";

        // Generate tools from EntityController<T> methods
        foreach (var method in GetControllerMethods(controllerType))
        {
            var comment = method.GetCustomAttribute<CommentAttribute>()?.Value ?? "";
            var description = _templateProcessor.Process(comment, entityType);

            yield return new McpToolDefinition
            {
                Name = prefix + GetActionName(method),
                Description = description,
                Schema = _schemaGenerator.Generate(method, entityType)
            };
        }
    }
}
```

#### 3. Template Processing

```csharp
public class TemplateProcessor
{
    private readonly Dictionary<string, Func<Type, string>> _replacements = new()
    {
        ["<TEntity>"] = type => type.Name,
        ["<TEntities>"] = type => Pluralize(type.Name),
        ["<entity>"] = type => type.Name.ToLowerInvariant(),
        ["<entities>"] = type => Pluralize(type.Name).ToLowerInvariant(),
    };

    public string Process(string template, Type entityType)
    {
        var result = template;
        foreach (var (placeholder, resolver) in _replacements)
        {
            result = result.Replace(placeholder, resolver(entityType));
        }
        return result;
    }
}
```

#### 4. Auto-Registration (Following Existing Patterns)

```csharp
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Mcp";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Configure MCP options with defaults
        services.Configure<McpServerOptions>(options =>
        {
            options.Enabled = true;
            options.Transport = "STDIO";
            options.Port = 8080;
        });

        // Discover MCP controllers using existing infrastructure
        var discoveredControllers = DiscoverMcpControllers();

        if (!discoveredControllers.Any())
        {
            // No MCP controllers found - skip registration
            return;
        }

        // Register core MCP services
        services.AddSingleton<IMcpServer, McpServer>();
        services.AddSingleton<McpControllerDiscovery>();
        services.AddSingleton<McpToolGenerator>();
        services.AddSingleton<TemplateProcessor>();
        services.AddSingleton<ToolExecutor>();

        // Register transport layers
        services.AddSingleton<StdioTransport>();
        services.AddSingleton<HttpSseTransport>();

        // Register as hosted service
        services.AddHostedService<McpServerHostedService>();
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var enabled = cfg.Read("Koan:Mcp:Enabled", true);
        var transport = cfg.Read("Koan:Mcp:Transport", "STDIO");

        var controllerCount = DiscoverMcpControllers().Count();

        report.AddSetting("MCP Enabled", enabled.ToString());
        report.AddSetting("MCP Transport", transport);
        report.AddSetting("MCP Controllers Discovered", controllerCount.ToString());
    }

    // Follow existing discovery pattern from other auto-registrars
    private IEnumerable<McpControllerInfo> DiscoverMcpControllers()
    {
        return AssemblyCache.Instance.GetAllAssemblies()
            .SelectMany(a => GetTypesFromAssembly(a))
            .Where(t => !t.IsAbstract && t.HasAttribute<McpControllerAttribute>())
            .Where(t => IsEntityController(t))
            .Select(t => new McpControllerInfo
            {
                ControllerType = t,
                EntityType = GetEntityType(t),
                Attribute = t.GetCustomAttribute<McpControllerAttribute>()
            });
    }

    // Reuse established safe type loading pattern
    private static Type[] GetTypesFromAssembly(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Cast<Type>().ToArray();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
}
```

## Usage Examples

### Basic Todo API to MCP Server

```bash
# 1. Create project and add Koan packages
dotnet new web -n TodoMcpApi && cd TodoMcpApi
dotnet add package Koan.Core
dotnet add package Koan.Web
dotnet add package Koan.Data.Sqlite
dotnet add package Koan.Mcp    # <-- Enables MCP automatically

# 2. Define entity
# Models/Todo.cs
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

# 3. Add controller
# Controllers/TodosController.cs
[ApiController]
[Route("api/[controller]")]
[McpController]  // <-- Only addition needed
public class TodosController : EntityController<Todo> { }

# 4. Run and MCP server is automatically available
dotnet run
```

**Generated MCP Tools:**
- `todo_list` - "List all Todo items with optional filtering and pagination"
- `todo_get` - "Retrieve a specific Todo by its unique identifier"
- `todo_create` - "Create a new Todo or update an existing one"
- `todo_delete` - "Delete a Todo by its identifier"

### Advanced Business Logic Integration

```csharp
[McpController]
public class ProjectsController : EntityController<Project>
{
    [Comment("Analyze <TEntity> risk factors using AI and historical data")]
    [HttpPost("{id}/analyze-risks")]
    public async Task<IActionResult> AnalyzeRisks(string id)
    {
        var project = await Project.Get(id);
        if (project == null) return NotFound();

        var risks = await _aiService.AnalyzeProjectRisks(project);
        return Ok(risks);
    }

    [Comment("Generate progress report for <TEntity> with team productivity metrics")]
    [HttpPost("{id}/progress-report")]
    public async Task<IActionResult> GenerateProgressReport(string id)
    {
        var project = await Project.Get(id);
        var metrics = await _analyticsService.GetProjectMetrics(project);
        var report = await _reportGenerator.CreateProgressReport(project, metrics);
        return Ok(report);
    }
}
```

**Generated Additional Tools:**
- `project_analyze_risks` - "Analyze Project risk factors using AI and historical data"
- `project_generate_progress_report` - "Generate progress report for Project with team productivity metrics"

### Configuration Options

```json
// appsettings.json
{
  "Koan": {
    "Mcp": {
      "Enabled": true,
      "Transport": "STDIO",           // STDIO, HTTP+SSE, WebSocket
      "Port": 8080,
      "EnabledControllers": ["TodosController", "ProjectsController"],
      "ToolPrefix": {
        "UseEntityName": true,
        "Format": "snake_case"        // snake_case, camelCase, PascalCase
      },
      "Authentication": {
        "Type": "OAuth2.1",
        "Scopes": ["api:read", "api:write"]
      }
    }
  }
}
```

## Generated MCP Tool Examples

### Tool Discovery Response

```json
{
  "jsonrpc": "2.0",
  "result": {
    "tools": [
      {
        "name": "todo_list",
        "description": "List all Todo items with optional filtering and pagination",
        "inputSchema": {
          "type": "object",
          "properties": {
            "completed": {"type": "boolean", "description": "Filter by completion status"},
            "page": {"type": "integer", "description": "Page number for pagination"},
            "size": {"type": "integer", "description": "Items per page"}
          }
        }
      },
      {
        "name": "todo_create",
        "description": "Create a new Todo or update an existing one",
        "inputSchema": {
          "type": "object",
          "properties": {
            "title": {"type": "string", "description": "Todo title"},
            "isCompleted": {"type": "boolean", "description": "Completion status"}
          },
          "required": ["title"]
        }
      },
      {
        "name": "project_analyze_risks",
        "description": "Analyze Project risk factors using AI and historical data",
        "inputSchema": {
          "type": "object",
          "properties": {
            "id": {"type": "string", "description": "Project identifier"}
          },
          "required": ["id"]
        }
      }
    ]
  }
}
```

### Tool Execution Example

```json
// LLM Request
{
  "jsonrpc": "2.0",
  "id": "req-001",
  "method": "tools/call",
  "params": {
    "name": "todo_create",
    "arguments": {
      "title": "Implement MCP integration",
      "isCompleted": false
    }
  }
}

// MCP Server Response
{
  "jsonrpc": "2.0",
  "id": "req-001",
  "result": {
    "content": [
      {
        "type": "text",
        "text": "Todo created successfully with ID: 01913b8c-8b2a-7000-8000-000000000000"
      }
    ],
    "isError": false
  }
}
```

## Benefits

### 1. Developer Experience
- **Zero Configuration**: Add package reference, get MCP functionality
- **Familiar Patterns**: Uses standard .NET attributes and conventions
- **Progressive Enhancement**: Start simple, add complexity as needed
- **IntelliSense Support**: Full type safety and code completion

### 2. Architecture Quality (DRY/KISS Principles)
- **Clean Separation**: EntityController remains MCP-agnostic
- **Reuses Existing Infrastructure**: Leverages `AssemblyCache`, established discovery patterns
- **No Code Duplication**: Follows proven reflection patterns from other auto-registrars
- **Minimal Complexity**: Simple template processing with established error handling
- **Framework Consistency**: Uses same patterns as background services, Flow, etc.

### 3. Enterprise Ready
- **Multi-Provider Support**: Works with any Koan data provider
- **Authentication Integration**: Leverages Koan's auth system
- **Monitoring & Observability**: Integrates with Koan's diagnostics
- **Container Native**: Works with Koan's orchestration tools

### 4. AI-First Design
- **Rich Tool Descriptions**: LLMs can understand and use tools effectively
- **Parameter Validation**: Automatic validation with meaningful error messages
- **Tool Discovery**: Dynamic tool listing with comprehensive metadata
- **Error Handling**: Graceful failures with actionable error messages

### 5. Implementation Efficiency (Leveraging Koan.Core)
- **Proven Reflection Infrastructure**: Uses `AssemblyCache.Instance` for assembly access
- **Established Error Handling**: Reuses `ReflectionTypeLoadException` patterns
- **Consistent Discovery**: Follows same attribute-based discovery as other modules
- **Reduced Testing Surface**: Core reflection logic already tested in Koan.Core

## Migration Path

### Phase 1: Core Implementation
- [ ] Implement `Koan.Mcp` module structure
- [ ] Add `CommentAttribute` to EntityController base methods
- [ ] Create tool discovery and generation services
- [ ] Implement STDIO transport for local development

### Phase 2: Enhanced Features
- [ ] Add HTTP+SSE transport for remote connections
- [ ] Implement authentication and authorization
- [ ] Add configuration options and environment detection
- [ ] Create comprehensive documentation and examples

### Phase 3: Advanced Integration
- [ ] Integrate with Koan AI services for enhanced tool capabilities
- [ ] Add Flow integration for event-driven MCP operations
- [ ] Implement resource providers for data access patterns
- [ ] Add monitoring and diagnostics for MCP operations

## Considerations

### Security
- OAuth 2.1 integration for remote connections
- Scope-based access control for tool execution
- Rate limiting and usage quotas
- Audit logging for tool executions

### Performance
- Tool discovery caching to avoid reflection overhead
- Async execution patterns for all tool operations
- Connection pooling for HTTP transport
- Efficient JSON serialization/deserialization

### Compatibility
- .NET 9+ required for advanced features
- Backward compatibility with existing EntityController implementations
- Support for custom validation attributes
- Integration with existing Koan middleware pipeline

## Conclusion

This proposal provides a comprehensive solution for integrating Koan APIs with the Model Context Protocol while maintaining clean architecture and excellent developer experience. The comment-attribute approach elegantly solves separation of concerns while providing rich tool descriptions through a simple template system.

The implementation follows **DRY and KISS principles** by leveraging Koan.Core's existing infrastructure:
- **AssemblyCache**: Eliminates duplicate assembly scanning code
- **Established Discovery Patterns**: Reuses proven type discovery from other auto-registrars
- **Safe Error Handling**: Leverages existing `ReflectionTypeLoadException` handling
- **Consistent Registration**: Follows same patterns as background services and other modules

The implementation follows Koan's core principles:
- **Reference = Intent**: Adding Koan.Mcp enables MCP functionality
- **Entity-First Development**: Leverages existing Entity<T> patterns
- **Multi-Provider Transparency**: Works across all Koan data providers
- **Zero Configuration**: Minimal code changes required
- **Framework Consistency**: Uses established infrastructure rather than reinventing

This positions Koan as a leader in AI-first API development, making it trivial for developers to expose their business logic to Large Language Models while maintaining production-ready architecture, security, and minimal implementation complexity through intelligent reuse of existing framework capabilities.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

namespace Koan.Web.Backup.Extensions;

/// <summary>
/// Extension methods for configuring Swagger/OpenAPI documentation for Koan Web Backup
/// </summary>
public static class SwaggerExtensions
{
    /// <summary>
    /// Add Swagger documentation for Koan Web Backup APIs
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="title">API title (optional)</param>
    /// <param name="version">API version (optional)</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddKoanWebBackupSwagger(
        this IServiceCollection services,
        string? title = null,
        string? version = null)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = title ?? "Koan Backup & Restore API",
                Version = version ?? "v1",
                Description = @"
## Comprehensive Backup & Restore API for Koan Framework

This API provides enterprise-grade backup and restore capabilities for Koan Framework applications,
offering both simple one-line operations and sophisticated enterprise-grade functionality.

### Key Features

- **🚀 Async Operations**: All backup/restore operations run asynchronously with real-time progress tracking
- **📊 Real-time Progress**: WebSocket-based progress updates via SignalR
- **🔍 Entity Discovery**: Automatic discovery and backup of all Entity<> types
- **⚡ Streaming Architecture**: Memory-efficient processing of large datasets
- **🗜️ Smart Compression**: ZIP compression with configurable levels
- **🛡️ Verification**: Built-in backup integrity verification
- **🔧 Adapter Optimization**: Database-specific optimizations for faster restores
- **📱 Multi-Provider**: Works across SQL, NoSQL, Vector, and JSON storage

### Usage Patterns

#### Simple Entity Backup
```http
POST /api/entities/User/backup
{
  ""name"": ""daily-users-backup"",
  ""description"": ""Daily backup of user data""
}
```

#### Global System Backup
```http
POST /api/backup/all
{
  ""name"": ""full-system-backup"",
  ""description"": ""Complete system backup"",
  ""tags"": [""production"", ""daily""]
}
```

#### Selective Backup
```http
POST /api/backup/selective
{
  ""name"": ""mongodb-only"",
  ""includeProviders"": [""mongo""],
  ""excludeEntityTypes"": [""AuditLog""]
}
```

#### Real-time Progress Tracking
Connect to the SignalR hub at `/api/backup/progress` to receive live updates:
```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl(""/api/backup/progress"")
    .build();

await connection.start();
await connection.invoke(""JoinOperationGroup"", operationId);
```

### Response Format

All operations return standardized responses with operation tracking:
```json
{
  ""operationId"": ""abc123"",
  ""backupName"": ""my-backup"",
  ""status"": ""Running"",
  ""statusUrl"": ""/api/backup/operations/abc123"",
  ""progress"": {
    ""percentComplete"": 45.2,
    ""currentStage"": ""Backing up entities"",
    ""itemsPerSecond"": 1250
  }
}
```
",
                Contact = new OpenApiContact
                {
                    Name = "Koan Framework",
                    Url = new Uri("https://github.com/your-org/koan-framework")
                },
                License = new OpenApiLicense
                {
                    Name = "MIT License",
                    Url = new Uri("https://opensource.org/licenses/MIT")
                }
            });

            // Include XML comments for detailed documentation
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            // Add operation tags for better organization
            options.TagActionsBy(api =>
            {
                var controllerName = api.ActionDescriptor.RouteValues["controller"];
                return controllerName switch
                {
                    "Backup" => new[] { "Backup Operations" },
                    "Restore" => new[] { "Restore Operations" },
                    "Entity" => new[] { "Entity-Specific Operations" },
                    _ => new[] { "General" }
                };
            });

            // Configure operation IDs for better client generation
            options.CustomOperationIds(apiDesc =>
            {
                var controller = apiDesc.ActionDescriptor.RouteValues["controller"];
                var action = apiDesc.ActionDescriptor.RouteValues["action"];
                return $"{controller}_{action}";
            });

            // Add security definitions (if needed for future authentication)
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "JWT Authorization header using the Bearer scheme."
            });

            // Add common response examples
            options.MapType<DateTimeOffset>(() => new OpenApiSchema
            {
                Type = "string",
                Format = "date-time",
                Example = new Microsoft.OpenApi.Any.OpenApiString("2025-01-23T10:30:00Z")
            });

            options.MapType<TimeSpan>(() => new OpenApiSchema
            {
                Type = "string",
                Format = "duration",
                Example = new Microsoft.OpenApi.Any.OpenApiString("01:23:45")
            });

            // Configure schema generation
            options.SchemaGeneratorOptions.SchemaIdSelector = type =>
            {
                // Use simple type names for cleaner schema IDs
                return type.Name;
            };

            // Add operation filters for additional metadata
            options.OperationFilter<BackupOperationFilter>();
        });

        return services;
    }
}

/// <summary>
/// Custom operation filter for adding backup-specific metadata to OpenAPI operations
/// </summary>
public class BackupOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Add common headers
        operation.Parameters ??= new List<OpenApiParameter>();

        // Add request ID header for tracking
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Request-ID",
            In = ParameterLocation.Header,
            Description = "Unique request identifier for tracking",
            Required = false,
            Schema = new OpenApiSchema { Type = "string", Format = "uuid" }
        });

        // Add common response headers
        if (operation.Responses.TryGetValue("202", out var acceptedResponse))
        {
            acceptedResponse.Headers ??= new Dictionary<string, OpenApiHeader>();

            acceptedResponse.Headers["X-Operation-Id"] = new OpenApiHeader
            {
                Description = "Unique operation identifier for tracking progress",
                Schema = new OpenApiSchema { Type = "string" }
            };

            acceptedResponse.Headers["Location"] = new OpenApiHeader
            {
                Description = "URL to check operation status",
                Schema = new OpenApiSchema { Type = "string", Format = "uri" }
            };
        }

        // Add common error responses
        if (!operation.Responses.ContainsKey("400"))
        {
            operation.Responses["400"] = new OpenApiResponse
            {
                Description = "Bad Request - Invalid input parameters",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["status"] = new OpenApiSchema { Type = "integer" },
                                ["title"] = new OpenApiSchema { Type = "string" },
                                ["detail"] = new OpenApiSchema { Type = "string" },
                                ["traceId"] = new OpenApiSchema { Type = "string" }
                            }
                        }
                    }
                }
            };
        }

        if (!operation.Responses.ContainsKey("500"))
        {
            operation.Responses["500"] = new OpenApiResponse
            {
                Description = "Internal Server Error - Unexpected error occurred",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["status"] = new OpenApiSchema { Type = "integer" },
                                ["title"] = new OpenApiSchema { Type = "string" },
                                ["detail"] = new OpenApiSchema { Type = "string" },
                                ["traceId"] = new OpenApiSchema { Type = "string" }
                            }
                        }
                    }
                }
            };
        }
    }
}
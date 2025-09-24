# Koan.Web.Backup

**ASP.NET Core Web API controllers for Koan Data Backup/Restore operations with real-time progress tracking and comprehensive management endpoints.**

## Overview

Koan.Web.Backup provides a complete RESTful API for backup and restore operations in Koan Framework applications. It transforms the powerful backend capabilities of Koan.Data.Backup into an accessible web API with real-time progress tracking, async operations, and enterprise-grade features.

## Key Features

- 🚀 **Async Operations**: All operations run asynchronously with operation tracking
- 📊 **Real-time Progress**: SignalR-based progress updates and notifications
- 🔍 **Entity Discovery**: Automatic discovery and management of all Entity<> types
- ⚡ **Streaming Architecture**: Memory-efficient processing of large datasets
- 🗜️ **Smart Compression**: ZIP compression with configurable levels
- 🛡️ **Verification**: Built-in backup integrity verification
- 🔧 **Adapter Optimization**: Database-specific optimizations for faster restores
- 📱 **Multi-Provider**: Works across SQL, NoSQL, Vector, and JSON storage
- 📈 **Progress Tracking**: Detailed progress information with ETAs
- 🎯 **Selective Operations**: Fine-grained control over what gets backed up/restored

## Quick Start

### 1. Installation

The module is automatically registered via `KoanAutoRegistrar` when you reference it. Simply add the project reference:

```xml
<ProjectReference Include="..\Koan.Web.Backup\Koan.Web.Backup.csproj" />
```

### 2. Configuration

Add to your `Program.cs`:

```csharp
// Services are auto-registered by KoanAutoRegistrar
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan(); // Includes Koan.Web.Backup automatically

var app = builder.Build();

// Configure the pipeline
app.UseKoanWebBackup(); // Adds SignalR hub and middleware
app.MapControllers();

app.Run();
```

### 3. Basic Usage

#### Simple Entity Backup
```http
POST /api/entities/User/backup
Content-Type: application/json

{
  "name": "daily-users-backup",
  "description": "Daily backup of user data"
}
```

#### Global System Backup
```http
POST /api/backup/all
Content-Type: application/json

{
  "name": "full-system-backup",
  "description": "Complete system backup",
  "tags": ["production", "daily"]
}
```

#### Check Operation Status
```http
GET /api/backup/operations/{operationId}
```

## API Reference

### Backup Operations

#### `POST /api/backup/all` - Global Backup
Create a backup of all entities in the system.

**Request Body:**
```json
{
  "name": "backup-name",
  "description": "Optional description",
  "tags": ["tag1", "tag2"],
  "compressionLevel": "Optimal",
  "maxConcurrency": 4,
  "includeProviders": ["mongo", "postgres"],
  "excludeEntityTypes": ["AuditLog"]
}
```

**Response:** `202 Accepted`
```json
{
  "operationId": "abc123def456",
  "backupName": "backup-name",
  "status": "Queued",
  "startedAt": "2025-01-23T10:30:00Z",
  "statusUrl": "/api/backup/operations/abc123def456",
  "cancelUrl": "/api/backup/operations/abc123def456/cancel"
}
```

#### `POST /api/backup/selective` - Selective Backup
Create a backup with specific filters.

#### `GET /api/backup/operations/{operationId}` - Operation Status
Get the current status and progress of a backup operation.

#### `POST /api/backup/operations/{operationId}/cancel` - Cancel Operation
Cancel a running backup operation.

#### `GET /api/backup/manifests` - List Backups
Get paginated list of available backups.

Query Parameters:
- `page`: Page number (default: 1)
- `pageSize`: Items per page (default: 20, max: 100)
- `tags`: Filter by tags
- `search`: Search term

#### `GET /api/backup/manifests/{backupId}` - Backup Details
Get detailed information about a specific backup.

#### `POST /api/backup/verify/{backupId}` - Verify Backup
Verify the integrity of a backup.

#### `GET /api/backup/status` - System Status
Get overall system status and health information.

### Restore Operations

#### `POST /api/restore/{backupName}` - Restore All
Restore all entities from a backup.

**Request Body:**
```json
{
  "backupName": "backup-to-restore",
  "replaceExisting": false,
  "disableConstraints": true,
  "disableIndexes": true,
  "useBulkMode": true,
  "optimizationLevel": "Balanced"
}
```

#### `POST /api/restore/{backupName}/test` - Test Restore Viability
Test if a restore operation would succeed without actually performing it.

#### `GET /api/restore/operations/{operationId}` - Restore Status
Get the current status and progress of a restore operation.

#### `POST /api/restore/operations/{operationId}/cancel` - Cancel Restore
Cancel a running restore operation.

#### `GET /api/restore/history` - Restore History
Get paginated history of restore operations.

### Entity-Specific Operations

#### `GET /api/entities` - List Entity Types
Get all discoverable entity types available for backup.

#### `POST /api/entities/{entityType}/backup` - Entity Backup
Create a backup of a specific entity type.

#### `POST /api/entities/{entityType}/restore/{backupName}` - Entity Restore
Restore a specific entity type from a backup.

#### `GET /api/entities/{entityType}/backups` - Entity Backup History
Get backup history for a specific entity type.

## Real-time Progress Tracking

### SignalR Hub

Connect to `/api/backup/progress` for real-time updates:

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/api/backup/progress")
    .build();

await connection.start();

// Join operation-specific updates
await connection.invoke("JoinOperationGroup", operationId);

// Listen for progress updates
connection.on("BackupProgress", (data) => {
    console.log(`Progress: ${data.progress.percentComplete}%`);
    console.log(`Stage: ${data.progress.currentStage}`);
    console.log(`ETA: ${data.progress.estimatedTimeRemaining}`);
});

// Listen for completion
connection.on("BackupCompleted", (data) => {
    console.log("Backup completed:", data.result);
});

// Listen for errors
connection.on("OperationFailed", (data) => {
    console.error("Operation failed:", data.errorMessage);
});
```

### Available Events

- `BackupProgress` - Backup progress updates
- `RestoreProgress` - Restore progress updates
- `BackupCompleted` - Backup completion notification
- `RestoreCompleted` - Restore completion notification
- `OperationFailed` - Operation failure notification
- `OperationCancelled` - Operation cancellation notification
- `SystemStatus` - System status updates

### Group Subscriptions

- `Operation_{operationId}` - Updates for specific operation
- `BackupUpdates` - All backup-related updates
- `RestoreUpdates` - All restore-related updates

## Configuration

### Service Registration

```csharp
// Basic registration (auto-registered by KoanAutoRegistrar)
services.AddKoanWebBackup();

// With persistent operation tracking
services.AddKoanWebBackupWithPersistentTracking();

// With enhanced notifications
services.AddKoanWebBackupWithEnhancedNotifications();

// With background cleanup services
services.AddKoanWebBackupBackgroundServices(
    cleanupInterval: TimeSpan.FromHours(1),
    maxOperationAge: TimeSpan.FromDays(7)
);
```

### Application Pipeline

```csharp
// Development configuration
app.UseKoanWebBackupDevelopment();

// Production configuration
app.UseKoanWebBackupProduction(allowedOrigins: new[] {
    "https://yourdomain.com"
});

// Custom configuration
app.UseKoanWebBackup(basePath: "/api/v1");
```

### Swagger Documentation

```csharp
services.AddKoanWebBackupSwagger(
    title: "My App Backup API",
    version: "v1.0"
);

// In pipeline
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Backup API V1");
    c.RoutePrefix = "docs/backup";
});
```

## Error Handling

The API uses standardized error responses:

```json
{
  "status": 400,
  "title": "Invalid Argument",
  "detail": "Backup name cannot be empty",
  "traceId": "00-abc123-def456-00"
}
```

Common status codes:
- `400` - Bad Request (invalid parameters)
- `404` - Not Found (backup/operation not found)
- `408` - Request Timeout (operation timeout)
- `409` - Conflict (operation already in progress)
- `500` - Internal Server Error

## Performance Considerations

### Concurrency

- Default max concurrency: `Environment.ProcessorCount`
- Recommended for large systems: 2-8 concurrent operations
- Monitor system resources during operations

### Memory Usage

- Streaming architecture minimizes memory usage
- Typical overhead: <100MB + streaming buffers
- Batch sizes can be adjusted for memory vs. performance trade-offs

### Network Optimization

- Use compression for better network efficiency
- Consider staging backups locally before transferring to remote storage
- SignalR uses efficient binary protocols for progress updates

## Security Considerations

### Authentication & Authorization

The API currently doesn't include built-in authentication. Add your authentication middleware:

```csharp
app.UseAuthentication();
app.UseAuthorization();

// Apply policies to backup controllers
services.AddAuthorization(options =>
{
    options.AddPolicy("BackupAdmin", policy =>
        policy.RequireRole("Admin", "BackupOperator"));
});
```

### CORS Configuration

Production CORS setup:

```csharp
app.UseKoanWebBackupProduction(allowedOrigins: new[] {
    "https://admin.yourdomain.com",
    "https://app.yourdomain.com"
});
```

### Data Protection

- Backups are stored using underlying Koan.Storage security
- Consider encryption at rest via storage provider configuration
- Use HTTPS for all API communications
- Implement request rate limiting for production environments

## Examples

### Complete C# Client Example

```csharp
public class BackupApiClient
{
    private readonly HttpClient _httpClient;
    private readonly HubConnection _hubConnection;

    public BackupApiClient(string baseUrl)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/api/backup/progress")
            .Build();
    }

    public async Task<string> StartGlobalBackupAsync(string name, string description = null)
    {
        var request = new
        {
            name,
            description,
            tags = new[] { "automated" },
            compressionLevel = "Optimal"
        };

        var response = await _httpClient.PostAsJsonAsync("/api/backup/all", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BackupOperationResponse>();
        return result.OperationId;
    }

    public async Task TrackProgressAsync(string operationId,
        Action<BackupProgressInfo> onProgress,
        Action<BackupManifest> onComplete)
    {
        await _hubConnection.StartAsync();
        await _hubConnection.InvokeAsync("JoinOperationGroup", operationId);

        _hubConnection.On<object>("BackupProgress", (data) =>
        {
            // Parse and handle progress
            onProgress?.Invoke(/* parsed progress */);
        });

        _hubConnection.On<object>("BackupCompleted", (data) =>
        {
            // Parse and handle completion
            onComplete?.Invoke(/* parsed result */);
        });
    }
}

// Usage
var client = new BackupApiClient("https://api.myapp.com");
var operationId = await client.StartGlobalBackupAsync("daily-backup-2025-01-23");

await client.TrackProgressAsync(operationId,
    progress => Console.WriteLine($"Progress: {progress.PercentComplete:F1}%"),
    result => Console.WriteLine($"Backup completed: {result.Id}")
);
```

### JavaScript/TypeScript Client Example

```typescript
interface BackupApiClient {
  startGlobalBackup(request: CreateGlobalBackupRequest): Promise<string>;
  trackProgress(operationId: string, callbacks: ProgressCallbacks): Promise<void>;
  getBackupStatus(operationId: string): Promise<BackupOperationResponse>;
}

class BackupClient implements BackupApiClient {
  private baseUrl: string;
  private connection: signalR.HubConnection;

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl;
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${baseUrl}/api/backup/progress`)
      .build();
  }

  async startGlobalBackup(request: CreateGlobalBackupRequest): Promise<string> {
    const response = await fetch(`${this.baseUrl}/api/backup/all`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request)
    });

    const result = await response.json();
    return result.operationId;
  }

  async trackProgress(operationId: string, callbacks: ProgressCallbacks): Promise<void> {
    await this.connection.start();
    await this.connection.invoke('JoinOperationGroup', operationId);

    this.connection.on('BackupProgress', callbacks.onProgress);
    this.connection.on('BackupCompleted', callbacks.onComplete);
    this.connection.on('OperationFailed', callbacks.onError);
  }
}
```

## Contributing

This module follows Koan Framework development patterns:

1. Controllers use dependency injection and follow RESTful conventions
2. All operations are async and use cancellation tokens
3. Progress tracking uses SignalR for real-time updates
4. Error handling follows RFC 7807 Problem Details standard
5. API documentation uses OpenAPI 3.0 with comprehensive examples

## License

This module is part of the Koan Framework and follows the same licensing terms.
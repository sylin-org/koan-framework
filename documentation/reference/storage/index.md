---
type: REFERENCE
domain: storage
title: "Storage Pillar Reference"
audience: [developers, architects, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Storage Pillar Reference

**Document Type**: REFERENCE
**Target Audience**: Developers, Architects
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## Installation

```bash
dotnet add package Koan.Storage
dotnet add package Koan.Web.Storage
```

```csharp
// Program.cs
builder.Services.AddKoan();
```

## Storage Objects

### Basic Storage Entity

```csharp
public class Document : Entity<Document>, IStorageObject
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string ContentHash { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string ProviderKey { get; set; } = "";
    public string BlobKey { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public Dictionary<string, string> CustomMetadata { get; set; } = new();
}
```

### File Upload

```csharp
[Route("api/[controller]")]
public class DocumentsController : StorageController<Document>
{
    // Inherits storage operations:
    // POST /api/documents - upload file
    // GET /api/documents/{id} - get metadata
    // GET /api/documents/{id}/content - download file
    // DELETE /api/documents/{id} - delete file
}
```

### Custom Upload Endpoint

```csharp
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IStorageService _storage;

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] string? profile = null)
    {
        var storageObject = new Document
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            Tags = ["uploaded", "user-content"]
        };

        using var stream = file.OpenReadStream();
        var result = await _storage.SaveAsync(storageObject, stream, profile);

        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var document = await Document.ById(id);
        return document == null ? NotFound() : Ok(document);
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(string id)
    {
        var document = await Document.ById(id);
        if (document == null) return NotFound();

        var stream = await _storage.OpenAsync(document);
        return File(stream, document.ContentType, document.FileName);
    }
}
```

## Storage Helpers

### Create Files

```csharp
public class FileService
{
    private readonly IStorageService _storage;

    // Create text file
    public async Task<Document> CreateTextFile(string content, string fileName)
    {
        var document = await _storage.CreateTextFile(
            key: $"texts/{fileName}",
            content: content,
            contentType: "text/plain"
        );

        return document;
    }

    // Create JSON file
    public async Task<Document> CreateJsonFile(object data, string fileName)
    {
        var document = await _storage.CreateJson(
            key: $"data/{fileName}",
            value: data,
            profile: "api-data"
        );

        return document;
    }

    // Upload from stream
    public async Task<Document> UploadFile(Stream stream, string fileName, string contentType)
    {
        var document = await _storage.Onboard(
            key: $"uploads/{fileName}",
            stream: stream,
            contentType: contentType
        );

        return document;
    }
}
```

### Read Files

```csharp
public class DocumentReader
{
    private readonly IStorageService _storage;

    // Read as text
    public async Task<string> ReadTextFile(string profile, string container, string key)
    {
        return await _storage.ReadAllText(profile, container, key);
    }

    // Read as bytes
    public async Task<byte[]> ReadBinaryFile(string profile, string container, string key)
    {
        return await _storage.ReadAllBytes(profile, container, key);
    }

    // Read range
    public async Task<string> ReadPartialFile(string profile, string container, string key, int start, int end)
    {
        return await _storage.ReadRangeAsString(profile, container, key, start, end);
    }

    // Check existence
    public async Task<bool> FileExists(string profile, string container, string key)
    {
        return await _storage.ExistsAsync(profile, container, key);
    }
}
```

## Configuration

### Single Profile (Development)

```json
{
  "Koan": {
    "Storage": {
      "DefaultProfile": "local",
      "Profiles": {
        "local": {
          "Provider": "local",
          "Container": "files",
          "BasePath": "./storage"
        }
      }
    }
  }
}
```

### Multiple Profiles with Rules

```json
{
  "Koan": {
    "Storage": {
      "DefaultProfile": "standard",
      "Profiles": {
        "standard": {
          "Provider": "local",
          "Container": "files",
          "BasePath": "./storage/standard",
          "Audit": true
        },
        "secure": {
          "Provider": "local",
          "Container": "secure",
          "BasePath": "./storage/secure",
          "Audit": true,
          "Encryption": true
        },
        "temp": {
          "Provider": "local",
          "Container": "temp",
          "BasePath": "./storage/temp",
          "Audit": false,
          "Retention": "7.00:00:00"
        }
      },
      "Rules": [
        {
          "When": {
            "TagsAny": ["secure", "confidential"]
          },
          "Use": "secure"
        },
        {
          "When": {
            "TagsAny": ["temp", "cache"]
          },
          "Use": "temp"
        },
        {
          "Default": true,
          "Use": "standard"
        }
      ]
    }
  }
}
```

### Environment Variables

```bash
# Default profile
Koan__Storage__DefaultProfile=production

# Profile configuration
Koan__Storage__Profiles__production__Provider=s3
Koan__Storage__Profiles__production__Container=prod-bucket
Koan__Storage__Profiles__production__BasePath=files/
```

## Storage Providers

### Local Provider

```csharp
// Local file system storage
{
  "Koan": {
    "Storage": {
      "Profiles": {
        "local": {
          "Provider": "local",
          "Container": "documents",
          "BasePath": "C:/Storage",
          "Shard": "hash2", // Directory sharding strategy
          "AuditEnabled": true
        }
      }
    }
  }
}
```

### Cloud Providers (Future)

```csharp
// S3-compatible storage
{
  "Koan": {
    "Storage": {
      "Profiles": {
        "s3": {
          "Provider": "s3",
          "Container": "my-bucket",
          "Region": "us-east-1",
          "AccessKey": "{S3_ACCESS_KEY}",
          "SecretKey": "{S3_SECRET_KEY}"
        }
      }
    }
  }
}

// Azure Blob Storage
{
  "Koan": {
    "Storage": {
      "Profiles": {
        "azure": {
          "Provider": "azure",
          "Container": "documents",
          "ConnectionString": "{AZURE_STORAGE_CONNECTION_STRING}"
        }
      }
    }
  }
}
```

## Pipeline Steps

### Content Validation

```csharp
public class ContentValidationStep : IStoragePipelineStep
{
    public async Task<StoragePipelineOutcome> OnReceiveAsync(StoragePipelineContext context)
    {
        // Validate file size
        if (context.Size > 10 * 1024 * 1024) // 10MB
        {
            return StoragePipelineOutcome.Stop("File too large");
        }

        // Validate content type
        var allowedTypes = new[] { "image/jpeg", "image/png", "application/pdf", "text/plain" };
        if (!allowedTypes.Contains(context.ContentType))
        {
            return StoragePipelineOutcome.Stop("Invalid content type");
        }

        return StoragePipelineOutcome.Continue;
    }

    public async Task<StoragePipelineOutcome> OnCommitAsync(StoragePipelineContext context)
    {
        // Additional validation after storage
        if (context.ContentHash != context.ExpectedHash)
        {
            return StoragePipelineOutcome.Quarantine("Hash mismatch detected");
        }

        return StoragePipelineOutcome.Continue;
    }
}
```

### Virus Scanning

```csharp
public class VirusScanStep : IStoragePipelineStep
{
    private readonly IVirusScanner _scanner;

    public async Task<StoragePipelineOutcome> OnReceiveAsync(StoragePipelineContext context)
    {
        // Scan during upload
        var scanResult = await _scanner.ScanStreamAsync(context.Stream);

        if (scanResult.IsInfected)
        {
            return StoragePipelineOutcome.Quarantine($"Virus detected: {scanResult.ThreatName}");
        }

        return StoragePipelineOutcome.Continue;
    }

    public Task<StoragePipelineOutcome> OnCommitAsync(StoragePipelineContext context)
    {
        // No action needed on commit
        return Task.FromResult(StoragePipelineOutcome.Continue);
    }
}
```

### Image Processing

```csharp
public class ImageProcessingStep : IStoragePipelineStep
{
    public async Task<StoragePipelineOutcome> OnReceiveAsync(StoragePipelineContext context)
    {
        if (!context.ContentType.StartsWith("image/"))
        {
            return StoragePipelineOutcome.Continue; // Skip non-images
        }

        // Generate thumbnail
        using var image = await Image.LoadAsync(context.Stream);
        using var thumbnail = image.Clone(x => x.Resize(new ResizeOptions
        {
            Size = new Size(200, 200),
            Mode = ResizeMode.Max
        }));

        // Store thumbnail with modified metadata
        var thumbnailKey = $"thumbnails/{Path.GetFileNameWithoutExtension(context.Key)}_thumb.jpg";
        context.CustomMetadata["ThumbnailKey"] = thumbnailKey;

        return StoragePipelineOutcome.Continue;
    }

    public Task<StoragePipelineOutcome> OnCommitAsync(StoragePipelineContext context)
    {
        return Task.FromResult(StoragePipelineOutcome.Continue);
    }
}
```

## File Transfers

### Cross-Profile Transfers

```csharp
public class FileArchiveService
{
    private readonly IStorageService _storage;

    // Move to cold storage
    public async Task ArchiveFile(string fileId)
    {
        var file = await Document.ById(fileId);
        if (file == null) return;

        await _storage.MoveTo(
            sourceProfile: "hot",
            sourceContainer: "",
            key: file.BlobKey,
            targetProfile: "cold"
        );

        // Update file record
        file.ProfileName = "cold";
        await file.Save();
    }

    // Copy for backup
    public async Task BackupFile(string fileId)
    {
        var file = await Document.ById(fileId);
        if (file == null) return;

        await _storage.CopyTo(
            sourceProfile: file.ProfileName,
            sourceContainer: "",
            key: file.BlobKey,
            targetProfile: "backup"
        );
    }

    // Transfer with custom logic
    public async Task TransferFile(string fileId, string targetProfile, bool deleteSource = false)
    {
        var file = await Document.ById(fileId);
        if (file == null) return;

        await _storage.TransferToProfileAsync(
            sourceProfile: file.ProfileName,
            sourceContainer: "",
            key: file.BlobKey,
            targetProfile: targetProfile,
            deleteSource: deleteSource
        );

        if (deleteSource)
        {
            file.ProfileName = targetProfile;
            await file.Save();
        }
    }
}
```

## Range Requests and Streaming

### Partial Content Support

```csharp
[HttpGet("{id}/stream")]
public async Task<IActionResult> StreamFile(string id, [FromHeader] string? range = null)
{
    var document = await Document.ById(id);
    if (document == null) return NotFound();

    if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes="))
    {
        // Parse range header: bytes=0-1023
        var rangeValue = range.Substring(6);
        var parts = rangeValue.Split('-');

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var start) &&
            int.TryParse(parts[1], out var end))
        {
            var partialStream = await _storage.OpenRangeAsync(document, start, end);

            Response.Headers.Add("Accept-Ranges", "bytes");
            Response.Headers.Add("Content-Range", $"bytes {start}-{end}/{document.Size}");

            return new FileStreamResult(partialStream, document.ContentType)
            {
                EnableRangeProcessing = true
            };
        }
    }

    // Full file stream
    var stream = await _storage.OpenAsync(document);
    return File(stream, document.ContentType, document.FileName);
}
```

## Audit and Monitoring

### Storage Audit

```csharp
public class StorageAuditSink : IStorageAuditSink
{
    private readonly ILogger<StorageAuditSink> _logger;

    public async Task RecordEventAsync(StorageAuditEvent auditEvent)
    {
        _logger.LogInformation("Storage event: {EventType} - {ObjectKey} by {UserId}",
            auditEvent.EventType, auditEvent.ObjectKey, auditEvent.UserId);

        // Store in audit log
        var log = new StorageAuditLog
        {
            EventType = auditEvent.EventType,
            ObjectKey = auditEvent.ObjectKey,
            UserId = auditEvent.UserId,
            Timestamp = auditEvent.Timestamp,
            Metadata = auditEvent.Metadata
        };

        await log.Save();
    }
}

public class StorageAuditLog : Entity<StorageAuditLog>
{
    public string EventType { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
```

### Health Monitoring

```csharp
public class StorageHealthCheck : IHealthContributor
{
    public string Name => "Storage";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            // Test storage operations
            var testKey = $"health-check-{Guid.NewGuid()}";
            var testContent = "Health check test";

            // Write test
            await _storage.CreateTextFile(testKey, testContent);

            // Read test
            var readContent = await _storage.ReadAllText("default", "", testKey);

            // Cleanup
            await _storage.TryDelete("default", "", testKey);

            var isHealthy = readContent == testContent;
            return new HealthReport(Name, isHealthy, isHealthy ? null : "Storage read/write test failed");
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, false, ex.Message);
        }
    }
}
```

## Testing

### Storage Testing

```csharp
[Test]
public async Task Should_Store_And_Retrieve_File()
{
    // Arrange
    var content = "Test file content";
    var fileName = "test.txt";

    // Act - Store
    var document = await _storage.CreateTextFile($"tests/{fileName}", content, "text/plain");

    // Act - Retrieve
    var retrievedContent = await _storage.ReadAllText(document.ProfileName, "", document.BlobKey);

    // Assert
    Assert.AreEqual(content, retrievedContent);
    Assert.AreEqual(fileName, document.FileName);
    Assert.AreEqual("text/plain", document.ContentType);
}

[Test]
public async Task Should_Apply_Storage_Rules()
{
    // Arrange
    var secureDocument = new Document
    {
        FileName = "secret.txt",
        ContentType = "text/plain",
        Tags = ["secure"]
    };

    // Act
    var result = await _storage.SaveAsync(secureDocument, new MemoryStream(Encoding.UTF8.GetBytes("secret")));

    // Assert
    Assert.AreEqual("secure", result.ProfileName); // Should route to secure profile
}
```

## API Reference

### Core Interfaces

```csharp
public interface IStorageService
{
    Task<T> SaveAsync<T>(T storageObject, Stream content, string? profile = null) where T : IStorageObject;
    Task<Stream> OpenAsync<T>(T storageObject) where T : IStorageObject;
    Task DeleteAsync<T>(T storageObject) where T : IStorageObject;
    Task<T> TransferToProfileAsync<T>(T storageObject, string targetProfile) where T : IStorageObject;
}

public interface IStorageObject
{
    string FileName { get; set; }
    string ContentType { get; set; }
    long Size { get; set; }
    string ContentHash { get; set; }
    string ProfileName { get; set; }
    string ProviderKey { get; set; }
    string BlobKey { get; set; }
    string[] Tags { get; set; }
    Dictionary<string, string> CustomMetadata { get; set; }
}
```

### Pipeline Outcomes

```csharp
public static class StoragePipelineOutcome
{
    public static StoragePipelineResult Continue => new(StoragePipelineAction.Continue);
    public static StoragePipelineResult Stop(string reason) => new(StoragePipelineAction.Stop, reason);
    public static StoragePipelineResult Quarantine(string reason) => new(StoragePipelineAction.Quarantine, reason);
    public static StoragePipelineResult Reroute(string profile) => new(StoragePipelineAction.Reroute, profile);
}
```

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+
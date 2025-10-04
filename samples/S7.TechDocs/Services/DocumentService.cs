using S7.TechDocs.Models;
using S7.TechDocs.Infrastructure;

using Koan.Data.Core.Model;

namespace S7.TechDocs.Services;

public class DocumentService : IDocumentService
{
    public DocumentService()
    {
        // Seed a minimal dataset the first time (JSON adapter will persist locally in Dev)
        _ = EnsureSeedAsync();
    }

    private static async Task EnsureSeedAsync()
    {
        var existing = await Document.Count;
        if (existing > 0) return;

        var seed = new List<Document>
        {
            new()
            {
                Id = "doc-001",
                Title = "Getting Started with Koan Framework",
                Content = @"# Getting Started with Koan Framework

Welcome to Koan! This guide will help you set up your first Koan application.

## Quick Setup

```bash
dotnet new Koan-api
dotnet add package Koan.Core
```

## Your First Controller

```csharp
[ApiController]
[Route(""api/[controller]"")]
public class ItemsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetItems()
    {
        var items = await Item.All(ct);
        return Ok(items);
    }
}
```

This creates a basic API with data access using Koan's first-class model methods.",
                Summary = "Complete setup guide for new Koan applications with examples",
                Status = Constants.DocumentStatus.Published,
                CollectionId = Constants.Collections.GettingStarted,
                AuthorId = "auth-001",
                AuthorName = "Alice Author",
                Tags = new() { "setup", "tutorial", "beginner", "framework" },
                CreatedAt = DateTime.UtcNow.AddDays(-7),
                UpdatedAt = DateTime.UtcNow.AddDays(-2),
                PublishedAt = DateTime.UtcNow.AddDays(-2),
                ViewCount = 245,
                Rating = 4.8,
                RatingCount = 23
            },
            new()
            {
                Id = "doc-002", 
                Title = "API Authentication Best Practices",
                Content = @"# API Authentication Best Practices

Learn how to secure your Koan APIs with proper authentication.

## TestProvider Setup

For development, use Koan's TestProvider:

```csharp
builder.Services.AddAuthentication(""TestProvider"")
    .AddScheme<TestAuthenticationSchemeOptions, TestAuthenticationHandler>(
        ""TestProvider"", options => { });
```

## Role-Based Authorization

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(""Author"", policy => policy.RequireRole(""Author""));
});
```",
                Summary = "Comprehensive guide to API security and authentication patterns",
                Status = Constants.DocumentStatus.Review,
                CollectionId = Constants.Collections.Guides,
                AuthorId = "auth-002",
                AuthorName = "Bob Builder",
                Tags = new() { "security", "authentication", "api", "authorization" },
                CreatedAt = DateTime.UtcNow.AddDays(-3),
                UpdatedAt = DateTime.UtcNow.AddHours(-6),
                ViewCount = 89,
                Rating = 4.2,
                RatingCount = 8,
                ReviewerId = "mod-001",
                ReviewNotes = "Good content, needs more examples for production setup"
            },
            new()
            {
                Id = "doc-003",
                Title = "Data Access Patterns",
                Content = @"# Data Access Patterns

This guide covers Koan's data access patterns and best practices.

## First-Class Model Methods

Always prefer first-class static methods for data access:

```csharp
// Preferred approach
var items = await Item.All(ct);
var filtered = await Item.Query(q => q.Where(x => x.IsActive), ct);

// Avoid generic facades when possible  
var items = await Data<Item, string>.All(ct);
```

## Streaming for Large Sets

```csharp
await foreach (var item in Item.AllStream(ct))
{
    // Process items one by one
}
```",
                Summary = "Essential patterns for data access using Koan's first-class model methods",
                Status = Constants.DocumentStatus.Draft,
                CollectionId = Constants.Collections.Guides,
                AuthorId = "auth-001",
                AuthorName = "Alice Author",
                Tags = new() { "data", "patterns", "database", "streaming" },
                CreatedAt = DateTime.UtcNow.AddHours(-4),
                UpdatedAt = DateTime.UtcNow.AddHours(-1),
                ViewCount = 12,
                Rating = 0,
                RatingCount = 0
            },
            new()
            {
                Id = "doc-004",
                Title = "Common Deployment Issues",
                Content = @"# Common Deployment Issues

Troubleshoot common problems when deploying Koan applications.

## Connection String Problems

Make sure your connection strings are properly configured:

```json
{
  ""Koan"": {
    ""Data"": {
      ""Postgres"": {
        ""ConnectionString"": ""Server=localhost;Database=myapp;""
      }
    }
  }
}
```

## Docker Container Issues

Common Dockerfile problems and solutions...",
                Summary = "Troubleshooting guide for common deployment problems",
                Status = Constants.DocumentStatus.Published,
                CollectionId = Constants.Collections.Troubleshooting,
                AuthorId = "mod-001",
                AuthorName = "Maya Moderator",
                Tags = new() { "deployment", "troubleshooting", "docker", "production" },
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                UpdatedAt = DateTime.UtcNow.AddDays(-1),
                PublishedAt = DateTime.UtcNow.AddDays(-1),
                ViewCount = 156,
                Rating = 4.5,
                RatingCount = 12
            },
            new()
            {
                Id = "doc-005",
                Title = "FAQ: Frequently Asked Questions",
                Content = @"# Frequently Asked Questions

## General Questions

**Q: What is Koan Framework?**
A: Koan is a greenfield framework for building modern, modular applications with emphasis on simplicity and flexibility.

**Q: How does it compare to other frameworks?**
A: Koan focuses on developer experience with first-class data access patterns and built-in AI capabilities.

## Technical Questions

**Q: Which databases are supported?**
A: Postgres, MongoDB, Redis, SQLite, and SQL Server are all supported through dedicated adapters.",
                Summary = "Common questions and answers about Koan Framework",
                Status = Constants.DocumentStatus.Published,
                CollectionId = Constants.Collections.Faq,
                AuthorId = "admin-001", 
                AuthorName = "Alex Admin",
                Tags = new() { "faq", "questions", "help", "general" },
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                UpdatedAt = DateTime.UtcNow.AddDays(-3),
                PublishedAt = DateTime.UtcNow.AddDays(-3),
                ViewCount = 567,
                Rating = 4.6,
                RatingCount = 45
            }
        };

        await Document.UpsertMany(seed);
    }

    public async Task<IEnumerable<Document>> GetAllAsync()
    {
        var items = await Document.All();
        return items.AsEnumerable();
    }

    public async Task<IEnumerable<Document>> GetByCollectionAsync(string collectionId)
    {
        var items = await Document.All();
        return items.Where(d => d.CollectionId == collectionId);
    }

    public async Task<IEnumerable<Document>> GetByStatusAsync(string status)
    {
        var items = await Document.All();
        return items.Where(d => d.Status == status);
    }

    public Task<Document?> GetByIdAsync(string id) => Document.Get(id);

    public async Task<Document> CreateAsync(Document document)
    {
        document.Id = string.IsNullOrWhiteSpace(document.Id) ? $"doc-{Guid.NewGuid():N}" : document.Id;
        document.CreatedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;
    await Document.Batch().Add(document).SaveAsync();
        return document;
    }

    public async Task<Document> UpdateAsync(Document document)
    {
        document.UpdatedAt = DateTime.UtcNow;
        await Document.Batch().Update(document.Id, d =>
        {
            d.Title = document.Title;
            d.Content = document.Content;
            d.Summary = document.Summary;
            d.Status = document.Status;
            d.CollectionId = document.CollectionId;
            d.AuthorId = document.AuthorId;
            d.AuthorName = document.AuthorName;
            d.Tags = document.Tags;
            d.PublishedAt = document.PublishedAt;
            d.ViewCount = document.ViewCount;
            d.Rating = document.Rating;
            d.RatingCount = document.RatingCount;
            d.ReviewerId = document.ReviewerId;
            d.ReviewNotes = document.ReviewNotes;
            d.UpdatedAt = document.UpdatedAt;
    }).SaveAsync();
        return document;
    }

    public Task DeleteAsync(string id) => Document.Remove(id);

}

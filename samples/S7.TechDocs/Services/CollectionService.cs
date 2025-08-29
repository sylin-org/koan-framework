using S7.TechDocs.Models;
using S7.TechDocs.Infrastructure;
using Sora.Data.Core.Model;

namespace S7.TechDocs.Services;

public class CollectionService : ICollectionService
{
    public CollectionService()
    {
        _ = EnsureSeedAsync();
    }

    private static async Task EnsureSeedAsync()
    {
        var existing = await Collection.Count();
        if (existing > 0) return;
        var seed = new List<Collection>
        {
            new()
            {
                Id = Constants.Collections.GettingStarted,
                Name = "Getting Started",
                Description = "Essential guides for new developers",
                Icon = "🚀",
                DocumentCount = 1,
                IsDefault = true
            },
            new()
            {
                Id = Constants.Collections.Guides,
                Name = "Developer Guides", 
                Description = "In-depth tutorials and best practices",
                Icon = "📚",
                DocumentCount = 2
            },
            new()
            {
                Id = Constants.Collections.ApiReference,
                Name = "API Reference",
                Description = "Complete API documentation and schemas",
                Icon = "🔧",
                DocumentCount = 0
            },
            new()
            {
                Id = Constants.Collections.Faq,
                Name = "FAQ",
                Description = "Frequently asked questions and answers",
                Icon = "❓",
                DocumentCount = 1
            },
            new()
            {
                Id = Constants.Collections.Troubleshooting,
                Name = "Troubleshooting",
                Description = "Common issues and their solutions",
                Icon = "🔍",
                DocumentCount = 1
            }
        };
        await Collection.UpsertMany(seed);
    }

    public Task<IEnumerable<Collection>> GetAllAsync()
    {
    return Collection.All().ContinueWith(t => (IEnumerable<Collection>)t.Result);
    }

    public Task<Collection?> GetByIdAsync(string id)
    {
        return Collection.Get(id);
    }
}
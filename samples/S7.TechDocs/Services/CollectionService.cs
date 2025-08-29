using S7.TechDocs.Models;
using S7.TechDocs.Infrastructure;
using Sora.Data.Core.Model;

namespace S7.TechDocs.Services;

public interface ICollectionService
{
    Task<IEnumerable<Collection>> GetAllAsync();
    Task<Collection?> GetByIdAsync(string id);
}

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

public interface IUserService
{
    Task<IEnumerable<User>> GetAllAsync();
    Task<User?> GetByIdAsync(string id);
    Task<User> UpdateRolesAsync(string id, List<string> roles);
}

public class UserService : IUserService
{
    public UserService()
    {
        _ = EnsureSeedAsync();
    }

    private static async Task EnsureSeedAsync()
    {
        var existing = await User.Count();
        if (existing > 0) return;
        var seed = new List<User>
        {
            new()
            {
                Id = "read-001",
                Name = "Rob Reader",
                Email = "rob@company.com",
                Roles = new() { Constants.Roles.Reader },
                LastActive = DateTime.UtcNow.AddHours(-2),
                DocumentsCreated = 0,
                DocumentsReviewed = 0
            },
            new()
            {
                Id = "auth-001",
                Name = "Alice Author",
                Email = "alice@company.com", 
                Roles = new() { Constants.Roles.Reader, Constants.Roles.Author },
                LastActive = DateTime.UtcNow.AddHours(-1),
                DocumentsCreated = 2,
                DocumentsReviewed = 0
            },
            new()
            {
                Id = "auth-002",
                Name = "Bob Builder",
                Email = "bob@company.com",
                Roles = new() { Constants.Roles.Reader, Constants.Roles.Author },
                LastActive = DateTime.UtcNow.AddHours(-6),
                DocumentsCreated = 1,
                DocumentsReviewed = 0
            },
            new()
            {
                Id = "mod-001",
                Name = "Maya Moderator",
                Email = "maya@company.com",
                Roles = new() { Constants.Roles.Reader, Constants.Roles.Author, Constants.Roles.Moderator },
                LastActive = DateTime.UtcNow.AddMinutes(-30),
                DocumentsCreated = 1,
                DocumentsReviewed = 3
            },
            new()
            {
                Id = "admin-001",
                Name = "Alex Admin",
                Email = "alex@company.com",
                Roles = new() { Constants.Roles.Reader, Constants.Roles.Author, Constants.Roles.Moderator, Constants.Roles.Admin },
                LastActive = DateTime.UtcNow.AddMinutes(-15),
                DocumentsCreated = 1,
                DocumentsReviewed = 5
            }
        };
        await User.UpsertMany(seed);
    }

    public Task<IEnumerable<User>> GetAllAsync()
    {
    return User.All().ContinueWith(t => (IEnumerable<User>)t.Result);
    }

    public Task<User?> GetByIdAsync(string id)
    {
        return User.Get(id);
    }

    public Task<User> UpdateRolesAsync(string id, List<string> roles)
    {
        return User.Get(id).ContinueWith(async t =>
        {
            var user = t.Result ?? throw new InvalidOperationException("User not found");
            await User.Batch().Update(id, u => u.Roles = roles).SaveAsync();
            return await User.Get(id) ?? user;
        }).Unwrap();
    }
}

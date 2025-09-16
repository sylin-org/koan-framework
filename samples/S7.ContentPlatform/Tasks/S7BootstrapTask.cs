using Microsoft.Extensions.Logging;
using S7.ContentPlatform.Models;
using Koan.Data.Core;
using Koan.Scheduling;

namespace S7.ContentPlatform.Tasks;

// One-off bootstrap seeding task; runs on startup and completes quickly.
internal sealed class S7BootstrapTask : IScheduledTask, IOnStartup, IHasTimeout
{
    private readonly ILogger<S7BootstrapTask>? _logger;

    public S7BootstrapTask(ILogger<S7.ContentPlatform.Tasks.S7BootstrapTask>? logger = null)
    {
        _logger = logger;
    }

    public string Id => "s7:bootstrap";

    public TimeSpan Timeout => TimeSpan.FromMinutes(2);

    public async Task RunAsync(CancellationToken ct)
    {
        _logger?.LogInformation("S7 bootstrap: ensuring demo dataset…");

        // Categories
        var categories = new[]
        {
            new Category { Id = "tech", Name = "Technology", Description = "Tech articles, tutorials, and industry insights", Slug = "technology", ColorHex = "#007acc", IconName = "code", SortOrder = 1 },
            new Category { Id = "business", Name = "Business", Description = "Business insights, strategy, and entrepreneurship", Slug = "business", ColorHex = "#28a745", IconName = "briefcase", SortOrder = 2 },
            new Category { Id = "lifestyle", Name = "Lifestyle", Description = "Life, culture, and personal development", Slug = "lifestyle", ColorHex = "#ffc107", IconName = "heart", SortOrder = 3 },
        };
        foreach (var c in categories) await Data<Category, string>.UpsertAsync(c, ct);

        // Authors
        var authors = new[]
        {
            new Author { Id = "alice", Name = "Alice Johnson", Email = "alice@contentplatform.com", Bio = "Senior Technology Writer with 8 years of experience in software development and technical writing.", Role = AuthorRole.Writer, SocialLinks = new Dictionary<string, string> { ["twitter"] = "@alicejohnson", ["linkedin"] = "alice-johnson-tech" } },
            new Author { Id = "editor", Name = "Sarah Editor", Email = "sarah@contentplatform.com", Bio = "Chief Editor with expertise in content strategy and editorial workflows.", Role = AuthorRole.Editor },
            new Author { Id = "bob", Name = "Bob Smith", Email = "bob@contentplatform.com", Bio = "Business Strategy Expert and startup mentor.", Role = AuthorRole.Writer },
            new Author { Id = "carol", Name = "Carol Davis", Email = "carol@contentplatform.com", Bio = "Lifestyle and Wellness Coach specializing in work-life balance.", Role = AuthorRole.Writer },
        };
        foreach (var a in authors) await Data<Author, string>.UpsertAsync(a, ct);

        // Articles (main/default set)
        var articles = new[]
        {
            new Article { Id = "getting-started-dotnet", Title = "Getting Started with .NET 9", Summary = "A comprehensive guide to the latest features in .NET 9", Content = "# Getting Started with .NET 9\n\n.NET 9 brings exciting new features including improved performance, enhanced C# 13 support, and better cloud-native capabilities...", AuthorId = "alice", CategoryId = "tech", Status = ArticleStatus.Published, PublishedAt = DateTime.UtcNow.AddDays(-7), Tags = new List<string> { "dotnet", "programming", "tutorial" }, Slug = "getting-started-dotnet-9", ReadingTimeMinutes = 8 },
            new Article { Id = "startup-funding-guide", Title = "Startup Funding in 2025", Summary = "Everything you need to know about raising capital in the current market", Content = "# Startup Funding in 2025\n\nThe funding landscape has evolved significantly. Here's what entrepreneurs need to know about securing investment...", AuthorId = "bob", CategoryId = "business", Status = ArticleStatus.Published, PublishedAt = DateTime.UtcNow.AddDays(-3), Tags = new List<string> { "startup", "funding", "investment" }, Slug = "startup-funding-guide-2025", ReadingTimeMinutes = 12 },
            new Article { Id = "mindful-productivity", Title = "Mindful Productivity Techniques", Summary = "Balance productivity with mental well-being", Content = "# Mindful Productivity\n\nIn our fast-paced world, maintaining productivity while preserving mental health is crucial...", AuthorId = "carol", CategoryId = "lifestyle", Status = ArticleStatus.Draft, Tags = new List<string> { "productivity", "mindfulness", "wellness" }, Slug = "mindful-productivity-techniques", ReadingTimeMinutes = 6 },
            new Article { Id = "ai-future-work", Title = "AI and the Future of Work", Summary = "How artificial intelligence will reshape employment and workplace dynamics", Content = "# AI and the Future of Work\n\nArtificial intelligence is transforming how we work. This article explores the implications...", AuthorId = "alice", CategoryId = "tech", Status = ArticleStatus.Draft, Tags = new List<string> { "ai", "future", "employment" }, Slug = "ai-future-of-work", ReadingTimeMinutes = 10 },
        };
        foreach (var ar in articles) await Data<Article, string>.UpsertAsync(ar, ct);

        // Moderation set: under review + rejected
        using (Data<Article, string>.WithSet("moderation"))
        {
            await Data<Article, string>.UpsertAsync(new Article { Id = "blockchain-explained", Title = "Blockchain Technology Explained", Summary = "A beginner's guide to understanding blockchain technology", Content = "# Blockchain Technology Explained\n\nBlockchain is a distributed ledger technology that enables secure, transparent transactions...", AuthorId = "alice", CategoryId = "tech", Status = ArticleStatus.UnderReview, Tags = new List<string> { "blockchain", "cryptocurrency", "technology" }, Slug = "blockchain-technology-explained", ReadingTimeMinutes = 15 }, ct);
            await Data<Article, string>.UpsertAsync(new Article { Id = "remote-work-trends", Title = "Remote Work Trends 2025", Summary = "Current trends in remote work and distributed teams", Content = "# Remote Work Trends 2025\n\nRemote work continues to evolve...", AuthorId = "bob", CategoryId = "business", Status = ArticleStatus.Rejected, EditorFeedback = "Good topic, but needs more data and recent statistics. Please add industry research and survey results.", Tags = new List<string> { "remote-work", "trends", "business" }, Slug = "remote-work-trends-2025", ReadingTimeMinutes = 7 }, ct);
        }

        // Deleted set: archived example
        using (Data<Article, string>.WithSet("deleted"))
        {
            await Data<Article, string>.UpsertAsync(new Article { Id = "outdated-tech-guide", Title = "Guide to Technology from 2020", Summary = "This article contains outdated information and has been archived", Content = "# Outdated Tech Guide\n\nThis content is no longer relevant...", AuthorId = "alice", CategoryId = "tech", Status = ArticleStatus.Archived, PublishedAt = DateTime.UtcNow.AddDays(-365), Tags = new List<string> { "legacy", "archived" }, Slug = "outdated-tech-guide", ReadingTimeMinutes = 5 }, ct);
        }

        _logger?.LogInformation("S7 bootstrap: demo dataset ensured.");
    }
}

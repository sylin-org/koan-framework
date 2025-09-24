using S10.DevPortal.Models;

namespace S10.DevPortal.Services;

/// <summary>
/// Implementation of demo data seeding service
/// </summary>
public class DemoSeedService : IDemoSeedService
{
    private readonly Random _random = new();

    public async Task<object> SeedDemoData()
    {
        // Create sample technology hierarchy
        var technologies = await GenerateSampleTechnologies();
        var techUpserted = await Technology.UpsertMany(technologies);

        // Create sample users
        var users = await GenerateSampleUsers();
        var usersUpserted = await User.UpsertMany(users);

        // Create sample articles with relationships
        var articles = await GenerateSampleArticlesWithRelationships(technologies, users, 50);
        var articlesUpserted = await Article.UpsertMany(articles);

        // Create sample comments
        var comments = await GenerateSampleComments(articles, users);
        var commentsUpserted = await Comment.UpsertMany(comments);

        return new
        {
            seeded = new
            {
                technologies = techUpserted,
                users = usersUpserted,
                articles = articlesUpserted,
                comments = commentsUpserted
            },
            message = "Demo data seeded successfully with full relationship graph"
        };
    }

    public async Task<List<Article>> GenerateSampleArticles(int count)
    {
        var articles = new List<Article>();
        var sampleTitles = new[]
        {
            "Getting Started with Entity Framework",
            "Advanced LINQ Queries",
            "Microservices Architecture Patterns",
            "Docker Container Best Practices",
            "API Design Guidelines",
            "Performance Optimization Techniques",
            "Database Design Principles",
            "Authentication and Authorization",
            "Error Handling Strategies",
            "Testing Methodologies"
        };

        for (int i = 0; i < count; i++)
        {
            var title = $"{sampleTitles[i % sampleTitles.Length]} #{i + 1}";
            articles.Add(new Article
            {
                Title = title,
                Content = $"This is sample content for {title}. " +
                         "Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
                         "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
                Type = _random.Next(0, 2) == 0 ? ResourceType.Article : ResourceType.Tutorial,
                IsPublished = _random.Next(0, 4) != 0, // 75% published
                CreatedAt = DateTime.UtcNow.AddDays(-_random.Next(0, 365))
            });
        }

        return articles;
    }

    public async Task<List<Technology>> GenerateSampleTechnologies()
    {
        var technologies = new List<Technology>();

        // Create parent technologies
        var dotnet = new Technology
        {
            Name = ".NET",
            Description = "Microsoft .NET Platform for building modern applications"
        };
        technologies.Add(dotnet);

        var javascript = new Technology
        {
            Name = "JavaScript",
            Description = "Dynamic programming language for web development"
        };
        technologies.Add(javascript);

        var database = new Technology
        {
            Name = "Databases",
            Description = "Data storage and management systems"
        };
        technologies.Add(database);

        // Save parent technologies first to get IDs
        var savedParents = await Technology.UpsertMany(technologies);

        // Create child technologies with parent relationships
        var childTechnologies = new List<Technology>
        {
            new Technology
            {
                Name = "ASP.NET Core",
                Description = "Cross-platform web framework for .NET",
                ParentId = dotnet.Id,
                RelatedIds = new List<string> { database.Id }
            },
            new Technology
            {
                Name = "Entity Framework",
                Description = "Object-relational mapping framework for .NET",
                ParentId = dotnet.Id,
                RelatedIds = new List<string> { database.Id }
            },
            new Technology
            {
                Name = "React",
                Description = "JavaScript library for building user interfaces",
                ParentId = javascript.Id
            },
            new Technology
            {
                Name = "Node.js",
                Description = "JavaScript runtime for server-side development",
                ParentId = javascript.Id
            },
            new Technology
            {
                Name = "MongoDB",
                Description = "NoSQL document database",
                ParentId = database.Id,
                RelatedIds = new List<string> { javascript.Id }
            },
            new Technology
            {
                Name = "PostgreSQL",
                Description = "Advanced open-source relational database",
                ParentId = database.Id,
                RelatedIds = new List<string> { dotnet.Id }
            }
        };

        technologies.AddRange(childTechnologies);
        return technologies;
    }

    public async Task<List<User>> GenerateSampleUsers()
    {
        var users = new List<User>
        {
            new User
            {
                Username = "alice.developer",
                Email = "alice@devportal.demo",
                DisplayName = "Alice Johnson",
                JoinedAt = DateTime.UtcNow.AddDays(-100)
            },
            new User
            {
                Username = "bob.architect",
                Email = "bob@devportal.demo",
                DisplayName = "Bob Smith",
                JoinedAt = DateTime.UtcNow.AddDays(-85)
            },
            new User
            {
                Username = "carol.lead",
                Email = "carol@devportal.demo",
                DisplayName = "Carol Williams",
                JoinedAt = DateTime.UtcNow.AddDays(-120)
            },
            new User
            {
                Username = "demo.user",
                Email = "demo@devportal.demo",
                DisplayName = "Demo User",
                JoinedAt = DateTime.UtcNow.AddDays(-1)
            }
        };

        return users;
    }

    public async Task<List<Comment>> GenerateSampleComments(List<Article> articles, List<User> users)
    {
        var comments = new List<Comment>();

        foreach (var article in articles.Take(10)) // Add comments to first 10 articles
        {
            var commentCount = _random.Next(1, 5); // 1-4 comments per article

            for (int i = 0; i < commentCount; i++)
            {
                var comment = new Comment
                {
                    ArticleId = article.Id,
                    UserId = users[_random.Next(users.Count)].Id,
                    Text = $"This is a sample comment #{i + 1} on '{article.Title}'. Great article!",
                    CreatedAt = article.CreatedAt.AddDays(_random.Next(1, 30))
                };
                comments.Add(comment);

                // 30% chance of reply to this comment
                if (_random.Next(0, 10) < 3)
                {
                    var reply = new Comment
                    {
                        ArticleId = article.Id,
                        UserId = users[_random.Next(users.Count)].Id,
                        ParentCommentId = comment.Id,
                        Text = $"Reply to comment #{i + 1}. Thanks for sharing your thoughts!",
                        CreatedAt = comment.CreatedAt.AddHours(_random.Next(1, 48))
                    };
                    comments.Add(reply);
                }
            }
        }

        return comments;
    }

    private async Task<List<Article>> GenerateSampleArticlesWithRelationships(List<Technology> technologies, List<User> users, int count)
    {
        var articles = new List<Article>();
        var sampleTitles = new[]
        {
            "Getting Started with {0}",
            "Advanced {0} Techniques",
            "Best Practices for {0}",
            "Performance Optimization in {0}",
            "Common Pitfalls in {0}",
            "Modern {0} Development",
            "Testing Strategies for {0}",
            "Debugging {0} Applications",
            "Scaling {0} Solutions",
            "Security in {0}"
        };

        for (int i = 0; i < count; i++)
        {
            var technology = technologies[_random.Next(technologies.Count)];
            var user = users[_random.Next(users.Count)];
            var titleTemplate = sampleTitles[i % sampleTitles.Length];
            var title = string.Format(titleTemplate, technology.Name);

            articles.Add(new Article
            {
                Title = title,
                Content = $"This is a comprehensive guide about {technology.Name}. " +
                         $"{technology.Description} " +
                         "Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
                         "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
                         "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.",
                Type = _random.Next(0, 2) == 0 ? ResourceType.Article : ResourceType.Tutorial,
                TechnologyId = technology.Id,
                AuthorId = user.Id,
                IsPublished = _random.Next(0, 4) != 0, // 75% published
                CreatedAt = DateTime.UtcNow.AddDays(-_random.Next(0, 365))
            });
        }

        return articles;
    }
}
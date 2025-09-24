using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Extensions;
using Koan.Data.Backup.Models;
using Koan.Data.Backup.Tests.TestEntities;
using Koan.Data.Core;
using Koan.Storage.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace Koan.Data.Backup.Tests.Fixtures;

public class BackupTestFixture : IDisposable
{
    private readonly IHost _host;
    private bool _disposed = false;

    public IServiceProvider ServiceProvider => _host.Services;
    public string TestStorageProfile { get; } = "test-storage";

    public BackupTestFixture()
    {
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Add logging
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

                // Mock storage service
                var mockStorageService = CreateMockStorageService();
                services.AddSingleton(mockStorageService);

                // Add backup/restore services
                services.AddKoanBackupRestore(options =>
                {
                    options.DefaultStorageProfile = TestStorageProfile;
                    options.WarmupEntitiesOnStartup = false;
                    options.EnableBackgroundDiscovery = false;
                    options.DefaultBatchSize = 100;
                });

                // Register test entities manually (normally done by Koan framework)
                RegisterTestEntities(services);
            });

        _host = hostBuilder.Build();
    }

    private IStorageService CreateMockStorageService()
    {
        var mock = new Mock<IStorageService>();

        // Setup storage operations for backup/restore testing
        var inMemoryStorage = new Dictionary<string, byte[]>();

        mock.Setup(s => s.PutAsync(
                It.IsAny<string>(), // storageProfile
                It.IsAny<string>(), // container
                It.IsAny<string>(), // key
                It.IsAny<Stream>(), // content
                It.IsAny<string>(), // contentType
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string profile, string container, string key, Stream content, string contentType, CancellationToken ct) =>
            {
                using var memoryStream = new MemoryStream();
                content.CopyTo(memoryStream);
                var fullKey = $"{profile}/{container}/{key}";
                inMemoryStorage[fullKey] = memoryStream.ToArray();

                return new TestStorageObject
                {
                    Key = key,
                    Size = memoryStream.Length,
                    ContentType = contentType,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ContentHash = ComputeHash(memoryStream.ToArray())
                };
            });

        mock.Setup(s => s.ReadAsync(
                It.IsAny<string>(), // storageProfile
                It.IsAny<string>(), // container
                It.IsAny<string>(), // key
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string profile, string container, string key, CancellationToken ct) =>
            {
                var fullKey = $"{profile}/{container}/{key}";
                if (inMemoryStorage.TryGetValue(fullKey, out var data))
                {
                    return new MemoryStream(data);
                }
                throw new FileNotFoundException($"Storage object not found: {fullKey}");
            });

        mock.Setup(s => s.ListAsync(
                It.IsAny<string>(), // storageProfile
                It.IsAny<string>(), // container
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string profile, string container, CancellationToken ct) =>
            {
                var prefix = $"{profile}/{container}/";
                return inMemoryStorage.Keys
                    .Where(k => k.StartsWith(prefix))
                    .Select(k => new TestStorageObject
                    {
                        Key = k.Substring(prefix.Length),
                        Size = inMemoryStorage[k].Length,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ContentHash = ComputeHash(inMemoryStorage[k])
                    })
                    .Cast<IStorageObject>()
                    .ToList();
            });

        return mock.Object;
    }

    private void RegisterTestEntities(IServiceCollection services)
    {
        // Mock entity configurations for testing
        // In a real scenario, these would be registered by the Koan framework
        var mockTestUserRepository = new Mock<IDataRepository<TestUser, Guid>>();
        var mockTestProductRepository = new Mock<IDataRepository<TestProduct, string>>();
        var mockTestOrderRepository = new Mock<IDataRepository<TestOrder, long>>();

        services.AddSingleton(mockTestUserRepository.Object);
        services.AddSingleton(mockTestProductRepository.Object);
        services.AddSingleton(mockTestOrderRepository.Object);
    }

    public async Task<List<TestUser>> CreateTestUsersAsync(int count = 10)
    {
        var users = new List<TestUser>();
        for (int i = 0; i < count; i++)
        {
            users.Add(new TestUser
            {
                Name = $"User {i + 1}",
                Email = $"user{i + 1}@example.com",
                Age = 20 + (i % 50),
                Tags = i % 2 == 0 ? new[] { "premium", "active" } : new[] { "standard" },
                Metadata = new Dictionary<string, object>
                {
                    { "department", i % 3 == 0 ? "engineering" : "sales" },
                    { "joinDate", DateTime.UtcNow.AddDays(-i * 30) }
                }
            });
        }
        return users;
    }

    public async Task<List<TestProduct>> CreateTestProductsAsync(int count = 5)
    {
        var products = new List<TestProduct>();
        var categories = new[] { "Electronics", "Books", "Clothing", "Home", "Sports" };

        for (int i = 0; i < count; i++)
        {
            products.Add(new TestProduct
            {
                Name = $"Product {i + 1}",
                Price = (decimal)(10.99 + (i * 5.50)),
                Category = categories[i % categories.Length],
                Quantity = 100 + i * 10
            });
        }
        return products;
    }

    public async Task<IBackupService> GetBackupServiceAsync()
    {
        return ServiceProvider.GetRequiredService<IBackupService>();
    }

    public async Task<IRestoreService> GetRestoreServiceAsync()
    {
        return ServiceProvider.GetRequiredService<IRestoreService>();
    }

    public async Task<IBackupDiscoveryService> GetBackupDiscoveryServiceAsync()
    {
        return ServiceProvider.GetRequiredService<IBackupDiscoveryService>();
    }

    public async Task<IEntityDiscoveryService> GetEntityDiscoveryServiceAsync()
    {
        return ServiceProvider.GetRequiredService<IEntityDiscoveryService>();
    }

    private static string ComputeHash(byte[] data)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _host?.Dispose();
            _disposed = true;
        }
    }
}

public class TestStorageObject : IStorageObject
{
    public string Id { get; set; } = Guid.CreateVersion7().ToString();
    public string Key { get; set; } = default!;
    public string? Name { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? ContentHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? Provider { get; set; }
    public string? Container { get; set; }
    public IReadOnlyDictionary<string, string>? Tags { get; set; }
}
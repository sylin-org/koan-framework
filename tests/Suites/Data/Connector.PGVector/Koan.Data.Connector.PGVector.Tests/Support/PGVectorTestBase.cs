using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using Koan.Core.Bootstrap.Abstractions;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Connector.PGVector.Tests.Support;

/// <summary>
/// Base class for PGVector tests providing PostgreSQL container lifecycle and test utilities.
/// Uses Testcontainers to spin up pgvector-enabled PostgreSQL instance.
/// </summary>
public abstract class PGVectorTestBase : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    protected IServiceProvider? ServiceProvider { get; private set; }
    protected string? ConnectionString { get; private set; }

    public async Task InitializeAsync()
    {
        // Build pgvector container
        _container = new PostgreSqlBuilder()
            .WithImage("pgvector/pgvector:pg16")
            .WithDatabase("test_db")
            .WithUsername("postgres")
            .WithPassword("test_password")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        // Start container
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();

        // Build service provider
        ServiceProvider = BuildServiceProvider(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }

    protected virtual IServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));

        // Add PGVector options
        services.Configure<PGVectorOptions>(options =>
        {
            options.ConnectionString = connectionString;
            options.DefaultDimension = 384; // MiniLM dimension for tests
            options.DefaultTopK = 10;
            options.AutoCreateIndex = true;
            options.DefaultIndexType = IndexType.Hnsw;
            options.HnswM = 16;
            options.HnswEfConstruction = 64;
        });

        // Add extension manager
        services.AddSingleton<PgVectorExtensionManager>();

        // Add factory
        services.AddSingleton<IVectorAdapterFactory, PGVectorAdapterFactory>();

        // Add mock AppHost for naming registry
        services.AddSingleton<IAppHost>(sp => new MockAppHost(sp));

        return services.BuildServiceProvider();
    }

    protected async Task<PGVectorRepository<TEntity, string>> CreateRepositoryAsync<TEntity>()
        where TEntity : class, IEntity<string>
    {
        if (ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider not initialized");

        var factory = ServiceProvider.GetRequiredService<IVectorAdapterFactory>() as PGVectorAdapterFactory;
        var repository = factory!.Create<TEntity, string>(ServiceProvider) as PGVectorRepository<TEntity, string>;

        // Ensure schema created
        await repository!.VectorEnsureCreated();

        return repository;
    }

    protected async Task<NpgsqlConnection> GetConnection()
    {
        if (ConnectionString == null)
            throw new InvalidOperationException("ConnectionString not initialized");

        var conn = new NpgsqlConnection(ConnectionString);
        await conn.Open();
        return conn;
    }

    protected float[] GenerateRandomEmbedding(int dimension = 384)
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var embedding = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            embedding[i] = (float)random.NextDouble();
        }

        // Normalize for cosine similarity
        var magnitude = 0f;
        for (int i = 0; i < dimension; i++)
        {
            magnitude += embedding[i] * embedding[i];
        }
        magnitude = (float)Math.Sqrt(magnitude);

        for (int i = 0; i < dimension; i++)
        {
            embedding[i] /= magnitude;
        }

        return embedding;
    }

    protected float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have same dimension");

        float dotProduct = 0f;
        float magnitudeA = 0f;
        float magnitudeB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        return dotProduct / ((float)Math.Sqrt(magnitudeA) * (float)Math.Sqrt(magnitudeB));
    }
}

/// <summary>
/// Test entity for vector search tests.
/// </summary>
public class Article : IEntity<string>
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

/// <summary>
/// Mock AppHost for testing (required by VectorStorageNameRegistry).
/// </summary>
internal class MockAppHost : IAppHost
{
    private readonly IServiceProvider _serviceProvider;

    public MockAppHost(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object? GetService(Type serviceType) => _serviceProvider.GetService(serviceType);
    public IServiceProvider ServiceProvider => _serviceProvider;
}

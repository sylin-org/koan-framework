using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Direct;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Vector;

public sealed class VectorWorkflowSpec
{
    private readonly ITestOutputHelper _output;

    public VectorWorkflowSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Save_Composes_document_and_vector_with_metadata()
    {
        await TestPipeline.For<VectorWorkflowSpec>(_output, nameof(Save_Composes_document_and_vector_with_metadata))
            .UsingServiceProvider("services", static (_, services) =>
            {
                var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
                services.AddSingleton<IConfiguration>(configuration);
                services.AddLogging();
                services.AddKoanDataCore();
                services.AddKoanDataVector();
                services.AddSingleton<FakeDataService>();
                services.AddSingleton<IDataService>(sp => sp.GetRequiredService<FakeDataService>());
                services.AddSingleton<FakeVectorService>();
                services.AddSingleton<IVectorService>(sp => sp.GetRequiredService<FakeVectorService>());
            })
            .Arrange(static _ =>
            {
                VectorProfiles.Register(builder => builder
                    .For<TestEntity>(Constants.DefaultProfile)
                        .WithMetadata(dict => dict["profile"] = "default"));
                return ValueTask.CompletedTask;
            })
            .Assert(async ctx =>
            {
                var provider = ctx.GetRequiredItem<ServiceProviderFixture>("services").Services;
                var previous = AppHost.Current;
                AppHost.Current = provider;
                try
                {
                    var dataService = provider.GetRequiredService<FakeDataService>();
                    var vectorService = provider.GetRequiredService<FakeVectorService>();
                    var entity = new TestEntity { Id = "alpha", Name = "demo" };
                    var metadata = new Dictionary<string, object?> { ["initial"] = "value" };

                    await VectorWorkflow<TestEntity>.Save(entity, new[] { 0.1f, 0.2f, 0.3f }, metadata, ct: CancellationToken.None);

                    dataService.Repository.LastUpsert.Should().NotBeNull();
                    vectorService.Repository.Upserts.Should().ContainSingle();
                    var recorded = vectorService.Repository.Upserts[0];
                    recorded.Id.Should().Be("alpha");
                    var recordedMetadata = recorded.Metadata.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
                    recordedMetadata.Should().ContainKey("profile");
                    recordedMetadata.Should().ContainKey("initial");
                }
                finally
                {
                    AppHost.Current = previous;
                }
            })
            .RunAsync();
    }

    [Fact]
    public async Task Query_uses_profile_defaults_when_configured()
    {
        await TestPipeline.For<VectorWorkflowSpec>(_output, nameof(Query_uses_profile_defaults_when_configured))
            .UsingServiceProvider("services", static (_, services) =>
            {
                var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
                services.AddSingleton<IConfiguration>(configuration);
                services.AddLogging();
                services.AddKoanDataCore();
                services.AddKoanDataVector();
                services.AddSingleton<FakeDataService>();
                services.AddSingleton<IDataService>(sp => sp.GetRequiredService<FakeDataService>());
                services.AddSingleton<FakeVectorService>();
                services.AddSingleton<IVectorService>(sp => sp.GetRequiredService<FakeVectorService>());
            })
            .Arrange(static _ =>
            {
                VectorProfiles.Register(builder => builder
                    .For<TestEntity>("meridian:evidence")
                        .TopK(12)
                        .Alpha(0.55));
                return ValueTask.CompletedTask;
            })
            .Assert(async ctx =>
            {
                var provider = ctx.GetRequiredItem<ServiceProviderFixture>("services").Services;
                var previous = AppHost.Current;
                AppHost.Current = provider;
                try
                {
                    var vectorService = provider.GetRequiredService<FakeVectorService>();
                    var workflow = VectorWorkflow<TestEntity>.For("meridian:evidence");
                    await workflow.Query(new[] { 0.42f, 0.13f, 0.9f }, text: "annual revenue", ct: CancellationToken.None);

                    vectorService.Repository.LastQuery.Should().NotBeNull();
                    vectorService.Repository.LastQuery!.TopK.Should().Be(12);
                    vectorService.Repository.LastQuery!.Alpha.Should().Be(0.55);
                    vectorService.Repository.LastQuery!.SearchText.Should().Be("annual revenue");
                }
                finally
                {
                    AppHost.Current = previous;
                }
            })
            .RunAsync();
    }

    [Fact]
    public async Task Data_SaveWithVector_delegates_to_workflow_profile()
    {
        await TestPipeline.For<VectorWorkflowSpec>(_output, nameof(Data_SaveWithVector_delegates_to_workflow_profile))
            .UsingServiceProvider("services", static (_, services) =>
            {
                var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
                services.AddSingleton<IConfiguration>(configuration);
                services.AddLogging();
                services.AddKoanDataCore();
                services.AddKoanDataVector();
                services.AddSingleton<FakeDataService>();
                services.AddSingleton<IDataService>(sp => sp.GetRequiredService<FakeDataService>());
                services.AddSingleton<FakeVectorService>();
                services.AddSingleton<IVectorService>(sp => sp.GetRequiredService<FakeVectorService>());
            })
            .Arrange(static _ =>
            {
                VectorProfiles.Register(builder => builder
                    .For<TestEntity>(Constants.DefaultProfile)
                        .WithMetadata(dict => dict["source"] = "workflow"));
                return ValueTask.CompletedTask;
            })
            .Assert(async ctx =>
            {
                var provider = ctx.GetRequiredItem<ServiceProviderFixture>("services").Services;
                var previous = AppHost.Current;
                AppHost.Current = provider;
                try
                {
                    var vectorService = provider.GetRequiredService<FakeVectorService>();
                    var entity = new TestEntity { Id = "delta", Name = "pipeline" };

                    await VectorData<TestEntity>.SaveWithVector(entity, new[] { 1.0f, 2.0f, 3.0f }, ct: CancellationToken.None);

                    vectorService.Repository.Upserts.Should().ContainSingle();
                    var metadata = vectorService.Repository.Upserts[0].Metadata.Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
                    metadata.Should().ContainKey("source");
                }
                finally
                {
                    AppHost.Current = previous;
                }
            })
            .RunAsync();
    }

    private static class Constants
    {
        public const string DefaultProfile = "default";
    }

    private sealed class FakeDataService : IDataService
    {
        public FakeDataRepository Repository { get; } = new();

        public IDataRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
        {
            if (typeof(TEntity) == typeof(TestEntity) && typeof(TKey) == typeof(string))
            {
                return (IDataRepository<TEntity, TKey>)(object)Repository;
            }

            throw new NotSupportedException("Unsupported repository request");
        }

        public IDirectSession Direct(string? source = null, string? adapter = null)
            => throw new NotSupportedException();

    }

        private sealed class FakeDataRepository : IDataRepository<TestEntity, string>
    {
        public TestEntity? LastUpsert { get; private set; }
    public IReadOnlyList<TestEntity>? LastUpsertMany { get; private set; }

        public Task<TestEntity?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<TestEntity?>(null);

        public Task<IReadOnlyList<TestEntity?>> GetManyAsync(IEnumerable<string> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TestEntity?>>(Array.Empty<TestEntity?>());

        public Task<IReadOnlyList<TestEntity>> QueryAsync(object? query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TestEntity>>(Array.Empty<TestEntity>());

        public Task<CountResult> CountAsync(CountRequest<TestEntity> request, CancellationToken ct = default)
            => Task.FromResult(new CountResult(0, false));

        public Task<TestEntity> UpsertAsync(TestEntity model, CancellationToken ct = default)
        {
            LastUpsert = model;
            return Task.FromResult(model);
        }

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(true);

        public Task<int> UpsertManyAsync(IEnumerable<TestEntity> models, CancellationToken ct = default)
        {
            var list = models as IList<TestEntity> ?? models.ToList();
            LastUpsertMany = list.ToList();
            return Task.FromResult(list.Count);
        }

        public Task<int> DeleteManyAsync(IEnumerable<string> ids, CancellationToken ct = default) => Task.FromResult(0);

        public Task<int> DeleteAllAsync(CancellationToken ct = default) => Task.FromResult(0);

        public Task<long> RemoveAllAsync(RemoveStrategy strategy, CancellationToken ct = default) => Task.FromResult(0L);

        public IBatchSet<TestEntity, string> CreateBatch() => throw new NotSupportedException();
    }

    private sealed class FakeVectorService : IVectorService
    {
        public FakeVectorRepository Repository { get; } = new();

        public IVectorSearchRepository<TEntity, TKey>? TryGetRepository<TEntity, TKey>()
            where TEntity : class, IEntity<TKey>
            where TKey : notnull
        {
            if (typeof(TEntity) == typeof(TestEntity) && typeof(TKey) == typeof(string))
            {
                return (IVectorSearchRepository<TEntity, TKey>)(object)Repository;
            }

            return null;
        }
    }

    private sealed class FakeVectorRepository : IVectorSearchRepository<TestEntity, string>
    {
        public List<(string Id, float[] Embedding, object? Metadata)> Upserts { get; } = new();
        public VectorQueryOptions? LastQuery { get; private set; }

        public Task UpsertAsync(string id, float[] embedding, object? metadata = null, CancellationToken ct = default)
        {
            Upserts.Add((id, embedding, metadata));
            return Task.CompletedTask;
        }

        public Task<int> UpsertManyAsync(IEnumerable<(string Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
        {
            var list = items as IList<(string Id, float[] Embedding, object? Metadata)> ?? items.ToList();
            Upserts.AddRange(list);
            return Task.FromResult(list.Count);
        }

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(true);

        public Task<int> DeleteManyAsync(IEnumerable<string> ids, CancellationToken ct = default) => Task.FromResult(0);

        public Task<VectorQueryResult<string>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
        {
            LastQuery = options;
            return Task.FromResult(new VectorQueryResult<string>(Array.Empty<VectorMatch<string>>(), null));
        }

        public Task VectorEnsureCreatedAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestEntity : Entity<TestEntity, string>
    {
        [Identifier]
        public override string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }
}

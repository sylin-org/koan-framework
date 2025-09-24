using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp;
using Koan.Mcp.Execution;
using Koan.Mcp.Extensions;
using Koan.Mcp.Options;
using Koan.Web.Endpoints;
using Koan.Web.Extensions;
using Koan.Web.Hooks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Mcp.Tests;

public sealed class McpEntityRegistryTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public McpEntityRegistryTests()
    {
        Koan.Core.Hosting.Bootstrap.AssemblyCache.Instance.AddAssembly(typeof(TestEntity).Assembly);
    }

    [Fact]
    public void Registry_discovers_entity_and_builds_schema()
    {
        using var provider = BuildServiceProvider();
        var registry = provider.GetRequiredService<McpEntityRegistry>();

        var registration = registry.Registrations.Single(r => r.EntityType == typeof(TestEntity));
        registration.DisplayName.Should().Be("TestEntity");
        registration.Tools.Should().Contain(t => t.Operation == EntityEndpointOperationKind.Collection);
        registration.Tools.Should().Contain(t => t.Operation == EntityEndpointOperationKind.Upsert);

        var collectionTool = registration.Tools.Single(t => t.Operation == EntityEndpointOperationKind.Collection);
        var properties = collectionTool.InputSchema["properties"]!.AsObject();
        properties.Should().ContainKey("filter");
        properties.Should().ContainKey("page");
        properties.Should().ContainKey("pageSize");

        var upsertTool = registration.Tools.Single(t => t.Operation == EntityEndpointOperationKind.Upsert);
        var modelSchema = upsertTool.InputSchema["properties"]!["model"]!.AsObject();
        var modelProps = modelSchema["properties"]!.AsObject();
        modelProps.Should().ContainKey(nameof(TestEntity.Name));
        modelProps[nameof(TestEntity.Name)]!.AsObject()["description"]!.GetValue<string>().Should().Be("Display name");
        modelSchema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Contain(nameof(TestEntity.Name));
    }

    [Fact]
    public void Registry_respects_mutation_flag()
    {
        using var provider = BuildServiceProvider();
        var registry = provider.GetRequiredService<McpEntityRegistry>();

        var registration = registry.Registrations.Single(r => r.EntityType == typeof(ReadOnlyEntity));
        registration.Tools.Should().OnlyContain(t => !t.IsMutation);
    }

    [Fact]
    public async Task Endpoint_executor_emits_parity_with_service_results()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();

        var services = scope.ServiceProvider;
        var executor = services.GetRequiredService<EndpointToolExecutor>();
        var registry = services.GetRequiredService<McpEntityRegistry>();
        var entityService = services.GetRequiredService<IEntityEndpointService<TestEntity, Guid>>();
        var contextBuilder = services.GetRequiredService<EntityRequestContextBuilder>();

        var queryContext = contextBuilder.Build(new QueryOptions(), CancellationToken.None);
        var directResult = await entityService.GetCollectionAsync(new EntityCollectionRequest
        {
            Context = queryContext,
            ForcePagination = false
        });

        var toolName = registry.Registrations.Single(r => r.EntityType == typeof(TestEntity))
            .Tools.Single(t => t.Operation == EntityEndpointOperationKind.Collection).Name;

        var mcpResult = await executor.ExecuteAsync(toolName, new JsonObject(), CancellationToken.None);

        mcpResult.Success.Should().BeTrue( $"Failure: {mcpResult.ErrorCode} {mcpResult.ErrorMessage} {mcpResult.Diagnostics.ToJsonString()}"); 
        var expectedPayload = JsonSerializer.SerializeToNode(directResult.Items, SerializerOptions)!.ToJsonString();
        mcpResult.Payload.Should().NotBeNull();
        mcpResult.Payload!.ToJsonString().Should().Be(expectedPayload);
        mcpResult.Headers.Should().ContainKey("X-Source").WhoseValue.Should().Be("stub");
        mcpResult.Warnings.Should().Contain("collection-warning");
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddKoanWeb();
        services.AddKoanMcp();
        services.Configure<McpServerOptions>(opts =>
        {
            opts.EnableStdioTransport = false;
        });

        services.AddSingleton<Koan.Data.Core.IAggregateIdentityManager, Koan.Data.Core.AggregateIdentityManager>();
        services.AddSingleton<Koan.Data.Abstractions.IDataAdapterFactory, StubDataAdapterFactory>();
        services.AddSingleton<IEntityEndpointService<TestEntity, Guid>, StubEntityService>();

        return services.BuildServiceProvider();
    }

    [Koan.Data.Abstractions.SourceAdapter("stub")]
    [McpEntity(Name = "TestEntity", Description = "Sample entity")]
    private sealed class TestEntity : Koan.Data.Abstractions.IEntity<Guid>
    {
        public Guid Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.Display(Description = "Display name")]
        public string Name { get; set; } = string.Empty;

        public int Quantity { get; set; }
        public bool IsActive { get; set; }
    }

    [Koan.Data.Abstractions.SourceAdapter("stub")]
    [McpEntity(Name = "ReadOnly", AllowMutations = false)]
    private sealed class ReadOnlyEntity : Koan.Data.Abstractions.IEntity<Guid>
    {
        public Guid Id { get; set; }
    }

    private sealed class StubEntityService : IEntityEndpointService<TestEntity, Guid>
    {
        private readonly List<TestEntity> _items = new()
        {
            new TestEntity { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Name = "Alpha", Quantity = 1, IsActive = true },
            new TestEntity { Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Name = "Beta", Quantity = 2, IsActive = false }
        };

        public Task<EntityCollectionResult<TestEntity>> GetCollectionAsync(EntityCollectionRequest request)
        {
            request.Context.Headers["X-Source"] = "stub";
            request.Context.Warn("collection-warning");
            return Task.FromResult(new EntityCollectionResult<TestEntity>(request.Context, _items, _items.Count, payload: null));
        }

        public Task<EntityCollectionResult<TestEntity>> QueryAsync(EntityQueryRequest request) => throw new NotImplementedException();

        public Task<EntityModelResult<TestEntity>> GetNewAsync(EntityGetNewRequest request) => throw new NotImplementedException();

        public Task<EntityModelResult<TestEntity>> GetByIdAsync(EntityGetByIdRequest<Guid> request)
        {
            var model = _items.FirstOrDefault(item => item.Id == request.Id);
            return Task.FromResult(new EntityModelResult<TestEntity>(request.Context, model, payload: null));
        }

        public Task<EntityModelResult<TestEntity>> UpsertAsync(EntityUpsertRequest<TestEntity> request) => throw new NotImplementedException();

        public Task<EntityEndpointResult> UpsertManyAsync(EntityUpsertManyRequest<TestEntity> request) => throw new NotImplementedException();

        public Task<EntityModelResult<TestEntity>> DeleteAsync(EntityDeleteRequest<Guid> request) => throw new NotImplementedException();

        public Task<EntityEndpointResult> DeleteManyAsync(EntityDeleteManyRequest<Guid> request) => throw new NotImplementedException();

        public Task<EntityEndpointResult> DeleteByQueryAsync(EntityDeleteByQueryRequest request) => throw new NotImplementedException();

        public Task<EntityEndpointResult> DeleteAllAsync(EntityDeleteAllRequest request) => throw new NotImplementedException();

        public Task<EntityModelResult<TestEntity>> PatchAsync(EntityPatchRequest<TestEntity, Guid> request) => throw new NotImplementedException();
    }

    private sealed class StubDataAdapterFactory : Koan.Data.Abstractions.IDataAdapterFactory
    {
        public bool CanHandle(string provider) => string.Equals(provider, "stub", StringComparison.OrdinalIgnoreCase);

        public Koan.Data.Abstractions.IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
            where TEntity : class, Koan.Data.Abstractions.IEntity<TKey>
            where TKey : notnull
            => throw new NotSupportedException("Repository access is not expected in MCP tests.");
    }
}


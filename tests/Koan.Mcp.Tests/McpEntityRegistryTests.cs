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
using Microsoft.AspNetCore.Mvc;
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
        modelProps[nameof(TestEntity.Quantity)]!.AsObject()["description"]!.GetValue<string>().Should().Be("Units available for MCP upserts");
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
            ForcePagination = false,
            Policy = Koan.Web.Attributes.PaginationPolicy.FromAttribute(new Koan.Web.Attributes.PaginationAttribute(), Koan.Web.Infrastructure.PaginationSafetyBounds.Default)
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

    [Fact]
    public async Task Endpoint_executor_surfaces_validation_short_circuit()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();

        var services = scope.ServiceProvider;
        var executor = services.GetRequiredService<EndpointToolExecutor>();
        var registry = services.GetRequiredService<McpEntityRegistry>();

        var toolName = registry.Registrations.Single(r => r.EntityType == typeof(TestEntity))
            .Tools.Single(t => t.Operation == EntityEndpointOperationKind.Upsert).Name;

        var invalidModel = new JsonObject
        {
            ["model"] = JsonSerializer.SerializeToNode(new
            {
                id = Guid.NewGuid(),
                name = string.Empty,
                quantity = 5,
                isActive = true
            }, SerializerOptions)
        };

        var result = await executor.ExecuteAsync(toolName, invalidModel, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Warnings.Should().Contain("validation-warning");
        result.ShortCircuit.Should().NotBeNull();
        var shortCircuit = result.ShortCircuit!.AsObject();
        shortCircuit["statusCode"]!.GetValue<int>().Should().Be(400);
        shortCircuit["type"]!.GetValue<string>().Should().Be(nameof(BadRequestObjectResult));
    }

    [Fact]
    public async Task Endpoint_executor_propagates_short_circuit_payload()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();

        var services = scope.ServiceProvider;
        var executor = services.GetRequiredService<EndpointToolExecutor>();
        var registry = services.GetRequiredService<McpEntityRegistry>();

        var toolName = registry.Registrations.Single(r => r.EntityType == typeof(TestEntity))
            .Tools.Single(t => t.Operation == EntityEndpointOperationKind.Query).Name;

        var arguments = new JsonObject { ["filter"] = "status:active" };

        var result = await executor.ExecuteAsync(toolName, arguments, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ShortCircuit.Should().NotBeNull();
        var shortCircuit = result.ShortCircuit!.AsObject();
        shortCircuit["reason"]!.GetValue<string>().Should().Be("query-short-circuit");
        shortCircuit["filter"]!.GetValue<string>().Should().Be("status:active");
    }

    [Fact]
    public async Task Endpoint_executor_handles_mutation_payload()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();

        var services = scope.ServiceProvider;
        var executor = services.GetRequiredService<EndpointToolExecutor>();
        var registry = services.GetRequiredService<McpEntityRegistry>();

        var toolName = registry.Registrations.Single(r => r.EntityType == typeof(TestEntity))
            .Tools.Single(t => t.Operation == EntityEndpointOperationKind.Upsert).Name;

        var modelId = Guid.NewGuid();
        var arguments = new JsonObject
        {
            ["model"] = JsonSerializer.SerializeToNode(new
            {
                id = modelId,
                name = "Gamma",
                quantity = 7,
                isActive = true
            }, SerializerOptions)
        };

        var result = await executor.ExecuteAsync(toolName, arguments, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Payload.Should().NotBeNull();
        var payload = result.Payload!.AsObject();
        payload[nameof(TestEntity.Name)]!.GetValue<string>().Should().Be("Gamma");
        payload[nameof(TestEntity.Quantity)]!.GetValue<int>().Should().Be(7);
        result.Headers.Should().ContainKey("X-Upsert").WhoseValue.Should().Be("stub");
    }

    [Fact]
    public async Task Endpoint_executor_respects_cancellation_token()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();

        var services = scope.ServiceProvider;
        var executor = services.GetRequiredService<EndpointToolExecutor>();
        var registry = services.GetRequiredService<McpEntityRegistry>();

        var deleteTool = registry.Registrations.Single(r => r.EntityType == typeof(TestEntity))
            .Tools.Single(t => t.Operation == EntityEndpointOperationKind.Delete).Name;

        var args = new JsonObject
        {
            ["id"] = JsonValue.Create("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ExecuteAsync(deleteTool, args, cts.Token));
    }

    [Fact]
    public void Registry_respects_http_sse_defaults()
    {
        using var provider = BuildServiceProvider();
        var registry = provider.GetRequiredService<McpEntityRegistry>();

        var registration = registry.Registrations.Single(r => r.EntityType == typeof(TestEntity));
        registration.EnableHttpSse.Should().BeTrue();
        registry.RegistrationsForHttpSse().Should().Contain(registration);
    }

    [Fact]
    public void Registry_respects_http_sse_override()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.Configure<McpServerOptions>(opts =>
            {
                opts.EntityOverrides["TestEntity"] = new McpEntityOverride
                {
                    EnabledTransports = McpTransportMode.Stdio
                };
            });
        });

        var registry = provider.GetRequiredService<McpEntityRegistry>();
        var registration = registry.Registrations.Single(r => r.EntityType == typeof(TestEntity));
        registration.EnabledTransports.Should().Be(McpTransportMode.Stdio);
        registry.RegistrationsForHttpSse().Should().BeEmpty();
    }

    [Fact]
    public void Registry_applies_require_authentication_override()
    {
        using var provider = BuildServiceProvider(services =>
        {
            services.Configure<McpServerOptions>(opts =>
            {
                opts.RequireAuthentication = true;
                opts.EntityOverrides["TestEntity"] = new McpEntityOverride
                {
                    RequireAuthentication = false
                };
            });
        });

        var registry = provider.GetRequiredService<McpEntityRegistry>();
        var registration = registry.Registrations.Single(r => r.EntityType == typeof(TestEntity));
        registration.RequireAuthentication.Should().BeFalse();
    }

    private static ServiceProvider BuildServiceProvider(Action<IServiceCollection>? configure = null)
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

        configure?.Invoke(services);

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

        [McpDescription("Units available for MCP upserts", Operation = EntityEndpointOperationKind.Upsert)]
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

        public Task<EntityCollectionResult<TestEntity>> QueryAsync(EntityQueryRequest request)
        {
            var shortCircuit = new { reason = "query-short-circuit", filter = request.FilterJson };
            return Task.FromResult(new EntityCollectionResult<TestEntity>(request.Context, _items, _items.Count, payload: null, shortCircuit: shortCircuit));
        }

        public Task<EntityModelResult<TestEntity>> GetNewAsync(EntityGetNewRequest request)
        {
            return Task.FromResult(new EntityModelResult<TestEntity>(request.Context, new TestEntity(), payload: null));
        }

        public Task<EntityModelResult<TestEntity>> GetByIdAsync(EntityGetByIdRequest<Guid> request)
        {
            var model = _items.FirstOrDefault(item => item.Id == request.Id);
            return Task.FromResult(new EntityModelResult<TestEntity>(request.Context, model, payload: null));
        }

        public Task<EntityModelResult<TestEntity>> UpsertAsync(EntityUpsertRequest<TestEntity> request)
        {
            if (string.IsNullOrWhiteSpace(request.Model.Name))
            {
                request.Context.Warn("validation-warning");
                var details = new ValidationProblemDetails(new Dictionary<string, string[]> { ["Name"] = new[] { "Name is required." } })
                {
                    Status = 400
                };
                var badRequest = new BadRequestObjectResult(details) { StatusCode = 400 };
                return Task.FromResult(new EntityModelResult<TestEntity>(request.Context, null, payload: null, shortCircuit: badRequest));
            }

            var existing = _items.FirstOrDefault(item => item.Id == request.Model.Id);
            if (existing is null)
            {
                existing = new TestEntity { Id = request.Model.Id == Guid.Empty ? Guid.NewGuid() : request.Model.Id };
                _items.Add(existing);
            }

            existing.Name = request.Model.Name;
            existing.Quantity = request.Model.Quantity;
            existing.IsActive = request.Model.IsActive;
            request.Context.Headers["X-Upsert"] = "stub";

            return Task.FromResult(new EntityModelResult<TestEntity>(request.Context, existing, payload: null));
        }

        public Task<EntityEndpointResult> UpsertManyAsync(EntityUpsertManyRequest<TestEntity> request) => throw new NotImplementedException();

        public async Task<EntityModelResult<TestEntity>> DeleteAsync(EntityDeleteRequest<Guid> request)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), request.Context.CancellationToken);
            var model = _items.FirstOrDefault(item => item.Id == request.Id);
            return new EntityModelResult<TestEntity>(request.Context, model, payload: null);
        }

        public Task<EntityEndpointResult> DeleteManyAsync(EntityDeleteManyRequest<Guid> request) => throw new NotImplementedException();

        public Task<EntityEndpointResult> DeleteByQueryAsync(EntityDeleteByQueryRequest request) => throw new NotImplementedException();

        public Task<EntityEndpointResult> DeleteAllAsync(EntityDeleteAllRequest request) => throw new NotImplementedException();

        public Task<EntityModelResult<TestEntity>> PatchAsync(EntityPatchRequest<TestEntity, Guid> request) => throw new NotImplementedException();
    }

    private sealed class StubDataAdapterFactory : Koan.Data.Abstractions.IDataAdapterFactory
    {
        public bool CanHandle(string provider) => string.Equals(provider, "stub", StringComparison.OrdinalIgnoreCase);

        public Koan.Data.Abstractions.IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp, string source = "Default")
            where TEntity : class, Koan.Data.Abstractions.IEntity<TKey>
            where TKey : notnull
            => throw new NotSupportedException("Repository access is not expected in MCP tests.");
    }
}


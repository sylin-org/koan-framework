using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Canon.Domain.Annotations;
using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Canon.Domain.Tests;

public class CanonRuntimeServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddCanonRuntime_ShouldApplyRegisteredConfigurators()
    {
        var services = new ServiceCollection();
        var persistence = new RecordingPersistence();
        services.AddSingleton(persistence);
        services.AddSingleton<ICanonRuntimeConfigurator, PipelineConfigurator>();
        services.AddSingleton<ICanonRuntimeConfigurator, DefaultOptionsConfigurator>();
        services.AddCanonRuntime();

        var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<ICanonRuntime>();

        var result = await runtime.Canonize(new TestCanonEntity());

        persistence.CanonicalSaveCount.Should().Be(1);
        result.Metadata.TryGetTag("configured", out var configured).Should().BeTrue();
        configured.Should().Be("true");
        result.Metadata.Origin.Should().Be("di-default");
        result.Events.Should().Contain(evt => evt.Phase == CanonPipelinePhase.Validation);
    }

    private sealed class PipelineConfigurator : ICanonRuntimeConfigurator
    {
        private readonly RecordingPersistence _persistence;

        public PipelineConfigurator(RecordingPersistence persistence)
        {
            _persistence = persistence;
        }

        public void Configure(CanonRuntimeBuilder builder)
        {
            builder.UsePersistence(_persistence);
            builder.ConfigurePipeline<TestCanonEntity>(pipeline =>
            {
                pipeline.AddStep(CanonPipelinePhase.Intake, (context, _) =>
                {
                    context.Metadata.SetTag("configured", "true");
                    return ValueTask.CompletedTask;
                }, "Intake step executed");

                pipeline.AddStep(CanonPipelinePhase.Validation, (context, _) =>
                {
                    context.Metadata.SetTag("origin", context.Options.Origin ?? "unknown");
                    return ValueTask.CompletedTask;
                }, "Validation step executed");
            });
        }
    }

    private sealed class DefaultOptionsConfigurator : ICanonRuntimeConfigurator
    {
        public void Configure(CanonRuntimeBuilder builder)
        {
            builder.ConfigureDefaultOptions(options => options.WithOrigin("di-default"));
        }
    }

    private sealed class RecordingPersistence : ICanonPersistence
    {
        public int CanonicalSaveCount { get; private set; }
        private readonly Dictionary<(string EntityType, string Key), CanonIndex> _indices = new();

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            CanonicalSaveCount++;
            return Task.FromResult(entity);
        }

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult(stage);

        public Task<CanonIndex?> GetIndexAsync(string entityType, string key, CancellationToken cancellationToken)
        {
            _indices.TryGetValue((entityType, key), out var index);
            return Task.FromResult(index);
        }

        public Task UpsertIndexAsync(CanonIndex index, CancellationToken cancellationToken)
        {
            _indices[(index.EntityType, index.Key)] = index;
            return Task.CompletedTask;
        }
    }

    private sealed class TestCanonEntity : CanonEntity<TestCanonEntity>
    {
        [AggregationKey]
        public string ExternalId { get; set; } = Guid.NewGuid().ToString("n");
    }
}

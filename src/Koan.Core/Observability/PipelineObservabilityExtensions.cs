using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Core.Hosting.App;
using Koan.Core.Pipelines;

namespace Koan.Core.Observability;

/// <summary>
/// Observability helpers for semantic pipelines.
/// </summary>
public static class PipelineObservabilityExtensions
{
    public static PipelineBuilder<TEntity> Trace<TEntity>(this IAsyncEnumerable<TEntity> source, Func<PipelineEnvelope<TEntity>, string> messageFactory)
        => source.Pipeline().Trace(messageFactory);

    public static PipelineBuilder<TEntity> Trace<TEntity>(this PipelineBuilder<TEntity> builder, Func<PipelineEnvelope<TEntity>, string> messageFactory)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (messageFactory is null) throw new ArgumentNullException(nameof(messageFactory));

        ILogger? ResolveLogger()
        {
            var provider = AppHost.Current;
            var factory = provider?.GetService<ILoggerFactory>();
            return factory?.CreateLogger($"Pipeline<{typeof(TEntity).Name}>");
        }

        return builder.AddStage((envelope, _) =>
        {
            var logger = ResolveLogger();
            if (logger is null)
            {
                return ValueTask.CompletedTask;
            }

            logger.LogInformation(messageFactory(envelope));
            return ValueTask.CompletedTask;
        });
    }

    public static TBuilder Trace<TEntity, TBuilder>(this IPipelineStageBuilder<TEntity, TBuilder> builder, Func<PipelineEnvelope<TEntity>, string> messageFactory)
        where TBuilder : IPipelineStageBuilder<TEntity, TBuilder>
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (messageFactory is null) throw new ArgumentNullException(nameof(messageFactory));

        ILogger? ResolveLogger()
        {
            var provider = AppHost.Current;
            var factory = provider?.GetService<ILoggerFactory>();
            return factory?.CreateLogger($"Pipeline<{typeof(TEntity).Name}>");
        }

        return builder.AddStage((envelope, _) =>
        {
            var logger = ResolveLogger();
            logger?.LogInformation(messageFactory(envelope));
            return ValueTask.CompletedTask;
        });
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Builder that composes pipelines and default behaviours for the canon runtime.
/// </summary>
public sealed class CanonRuntimeBuilder
{
    private CanonizationOptions _defaultOptions = CanonizationOptions.Default;
    private readonly Dictionary<Type, ICanonPipelineDescriptor> _descriptors = new();
    private int _recordCapacity = 1024;
    private ICanonPersistence _persistence = new DefaultCanonPersistence();

    /// <summary>
    /// Configures default options used when callers do not supply explicit overrides.
    /// </summary>
    public CanonRuntimeBuilder ConfigureDefaultOptions(Func<CanonizationOptions, CanonizationOptions> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        _defaultOptions = configure(_defaultOptions.Copy());
        return this;
    }

    /// <summary>
    /// Configures the pipeline contributors for a canonical entity type.
    /// Subsequent invocations replace the previous configuration for the same type.
    /// </summary>
    public CanonRuntimeBuilder ConfigurePipeline<TModel>(Action<CanonPipelineBuilder<TModel>> configure)
        where TModel : CanonEntity<TModel>, new()
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var builder = new CanonPipelineBuilder<TModel>();
        configure(builder);
    _descriptors[typeof(TModel)] = builder.Build();
        return this;
    }

    /// <summary>
    /// Overrides the persistence strategy used by the runtime.
    /// </summary>
    public CanonRuntimeBuilder UsePersistence(ICanonPersistence persistence)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        return this;
    }

    /// <summary>
    /// Sets the in-memory canonization record retention capacity.
    /// </summary>
    public CanonRuntimeBuilder SetRecordCapacity(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Record capacity must be greater than zero.");
        }

        _recordCapacity = capacity;
        return this;
    }

    /// <summary>
    /// Builds the runtime configuration object that can be registered in DI.
    /// </summary>
    public CanonRuntimeConfiguration BuildConfiguration()
    {
        var descriptors = new Dictionary<Type, ICanonPipelineDescriptor>(_descriptors);
        var metadata = descriptors.ToDictionary(static pair => pair.Key, static pair => pair.Value.Metadata);
        return new CanonRuntimeConfiguration(_defaultOptions.Copy(), descriptors, metadata, _recordCapacity, _persistence);
    }

    /// <summary>
    /// Builds a runtime instance using the configured pipelines.
    /// </summary>
    public CanonRuntime Build()
        => new(BuildConfiguration());
}

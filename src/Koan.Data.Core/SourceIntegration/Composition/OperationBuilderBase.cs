using System.ComponentModel;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core;

public abstract class OperationBuilderBase<TBuilder>
    where TBuilder : OperationBuilderBase<TBuilder>
{
    private readonly List<OperationParameter> _parameters = [];
    private string? _lane;
    private IDataOperationBinding? _binding;
    private int? _maxRecords;
    private long? _maxBytes;
    private long? _maxValueBytes;
    private TimeSpan? _timeout;

    protected OperationBuilderBase(string source, string name, OperationResultKind result, Type? scalarType)
    {
        Source = source;
        Name = name;
        Result = result;
        ScalarType = scalarType;
    }

    protected string Source { get; }
    protected string Name { get; }
    protected OperationResultKind Result { get; }
    protected Type? ScalarType { get; }

    public TBuilder Lane(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_lane is not null) throw new InvalidOperationException("An operation can select one read lane.");
        _lane = name.Trim();
        return (TBuilder)this;
    }

    public TBuilder Parameter<T>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_parameters.Any(parameter => string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Operation parameter '{name}' is declared more than once.");
        _parameters.Add(new OperationParameter(name.Trim(), typeof(T)));
        return (TBuilder)this;
    }

    public TBuilder MaxValueBytes(long value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        _maxValueBytes = value;
        return (TBuilder)this;
    }

    public TBuilder Timeout(TimeSpan value)
    {
        if (value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value));
        _timeout = value;
        return (TBuilder)this;
    }

    /// <summary>Provider/family extension hook used by binding leaves such as Sql, Pipeline, or Template.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TBuilder Native(IDataOperationBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (_binding is not null) throw new InvalidOperationException("An operation can have one native binding.");
        _binding = binding;
        return (TBuilder)this;
    }

    protected void SetMaxRecords(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        _maxRecords = value;
    }

    protected void SetMaxBytes(long value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        _maxBytes = value;
    }

    internal OperationPlan Build()
    {
        var defaults = DataOperationCatalog.DefaultLimits;
        var maxRecords = Result == OperationResultKind.Scalar ? 1 : _maxRecords ?? defaults.MaxRecords;
        var maxValue = _maxValueBytes ?? defaults.MaxValueBytes;
        var limits = new RecordSetLimits(
            maxRecords,
            Result == OperationResultKind.Scalar ? Math.Max(maxValue, 64) : _maxBytes ?? defaults.MaxBytes,
            maxValue,
            defaults.MaxDuration);
        limits.Validate();
        return new OperationPlan(
            Source,
            Name,
            DataOperationEffect.Read,
            Result,
            OperationDelivery.Buffered,
            Array.AsReadOnly(_parameters.ToArray()),
            _binding ?? throw new InvalidOperationException(
                $"Registered operation '{Name}' requires one provider binding such as Sql, Pipeline, Template, or Function."),
            _lane,
            null,
            limits,
            _timeout ?? defaults.MaxDuration,
            ScalarType);
    }
}

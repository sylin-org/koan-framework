namespace Koan.Data.Abstractions;

/// <summary>A typed pre-dispatch failure for a value that cannot satisfy a compiled mapping.</summary>
public sealed class MappingValueException : InvalidOperationException
{
    public MappingValueException(string planId, string bindingId, string correction, Exception? inner = null)
        : base($"Mapping plan '{planId}' rejected binding '{bindingId}'. {correction}", inner)
    {
        PlanId = planId;
        BindingId = bindingId;
        Correction = correction;
    }

    public string PlanId { get; }
    public string BindingId { get; }
    public string Correction { get; }
}

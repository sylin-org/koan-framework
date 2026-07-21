namespace Koan.Data.Core.Lifecycle;

/// <summary>The persistence operations exposed to entity lifecycle behavior.</summary>
public enum EntityLifecycleOperation
{
    Load,
    Upsert,
    Remove
}

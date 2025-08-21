namespace Sora.Data.Abstractions.Annotations;

/// <summary>
/// Signals that a property was previously stored under a different name, helping adapters during rebuild/copy migrations.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class RenamedFromAttribute : Attribute
{
    public RenamedFromAttribute(string priorName) => PriorName = priorName;

    public string PriorName { get; }
}

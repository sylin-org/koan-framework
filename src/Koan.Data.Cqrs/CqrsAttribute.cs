namespace Koan.Data.Cqrs;

/// <summary>
/// Enables CQRS behavior for an entity.
/// When present, the entity participates in the configured CQRS profile.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CqrsAttribute : Attribute
{
    /// <summary>
    /// Optional profile name. When null, the default profile from configuration is used.
    /// </summary>
    public string? Profile { get; }

    public CqrsAttribute() { }
    public CqrsAttribute(string profile) { Profile = profile; }
}

namespace Koan.Data.Abstractions;

/// <summary>
/// Marks an entity whose lifecycle hooks are a mandatory semantic/security boundary. Data refuses mutation modes
/// that explicitly bypass lifecycle for such an entity; callers must use a lifecycle-preserving operation.
/// </summary>
public interface IRequiresLifecycleEnforcement;


namespace Koan.Core.Adapters;

/// <summary>
/// Marker attribute for orchestration-aware methods
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class OrchestrationAwareAttribute : Attribute
{
}
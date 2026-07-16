namespace Koan.Core;

/// <summary>
/// Gives a framework provider a deterministic fallback rank. Higher values win after explicit intent and
/// capability eligibility; stable provider identity breaks ties.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ProviderPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}

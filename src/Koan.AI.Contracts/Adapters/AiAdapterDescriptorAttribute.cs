using System;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Declares metadata used during adapter election.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AiAdapterDescriptorAttribute : Attribute
{
    public AiAdapterDescriptorAttribute(int priority = 0)
    {
        Priority = priority;
    }

    /// <summary>
    /// Higher values win when automatically electing an adapter. Defaults to <c>0</c>.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Weighted round-robin weight used when multiple adapters share the same priority. Defaults to 1.
    /// Values lower than 1 are coerced to 1.
    /// </summary>
    public int Weight { get; init; } = 1;
}

namespace Koan.Core.Ordering;

/// <summary>
/// Declares that the annotated <see cref="IKoanInitializer"/> (typically an
/// <see cref="IKoanAutoRegistrar"/>) must have its
/// <see cref="IKoanInitializer.Initialize"/> invoked AFTER every type listed
/// in <see cref="Targets"/>. See CORE-0091.
/// </summary>
/// <remarks>
/// Companion to <see cref="BeforeAttribute"/>; see its remarks for the
/// equivalence and validation contract.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class AfterAttribute : Attribute
{
    public Type[] Targets { get; }

    public AfterAttribute(params Type[] targets)
    {
        Targets = targets ?? Array.Empty<Type>();
    }
}

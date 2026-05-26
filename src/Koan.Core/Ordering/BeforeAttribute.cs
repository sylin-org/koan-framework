namespace Koan.Core.Ordering;

/// <summary>
/// Declares that the annotated <see cref="IKoanInitializer"/> (typically an
/// <see cref="IKoanAutoRegistrar"/>) must have its
/// <see cref="IKoanInitializer.Initialize"/> invoked BEFORE every type listed in
/// <see cref="Targets"/>. See CORE-0091.
/// </summary>
/// <remarks>
/// <para>
/// Companion to <see cref="AfterAttribute"/>; the two are inverses
/// (<c>[Before(typeof(B))]</c> on A produces the same edge as <c>[After(typeof(A))]</c>
/// on B). Either form is valid — pick whichever places the constraint closest
/// to the authoritative knowledge.
/// </para>
/// <para>
/// Multiple applications on the same type are additive: each
/// <c>[Before(...)]</c> can carry one or more targets, and the constraint set
/// is the union.
/// </para>
/// <para>
/// Targets that are not assignable to <see cref="IKoanInitializer"/> throw at
/// sort time with the offending pair named. Targets that resolve to a type
/// not present in the registry (e.g., the referenced assembly was not loaded)
/// are silently skipped — that preserves "reference = intent": if you want
/// the ordering enforced, reference the module.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class BeforeAttribute : Attribute
{
    public Type[] Targets { get; }

    public BeforeAttribute(params Type[] targets)
    {
        Targets = targets ?? Array.Empty<Type>();
    }
}

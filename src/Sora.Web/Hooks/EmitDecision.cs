namespace Sora.Web.Hooks;

/// <summary>
/// Emission decision allows replacing or continuing the payload pipeline.
/// </summary>
public abstract record EmitDecision
{
    public sealed record Continue() : EmitDecision;
    public sealed record Replace(object Payload) : EmitDecision;
    public static Continue Next() => new();
    public static Replace With(object payload) => new(payload);
}
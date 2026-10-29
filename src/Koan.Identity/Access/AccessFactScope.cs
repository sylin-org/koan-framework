namespace Koan.Identity.Access;

/// <summary>The scope of an <see cref="AccessFact"/> that no tenant owns.</summary>
public static class AccessFactScope
{
    /// <summary>The fact holds outside any tenant.</summary>
    public const string Global = "global";
}

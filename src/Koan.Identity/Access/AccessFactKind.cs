namespace Koan.Identity.Access;

/// <summary>The kinds of <see cref="AccessFact"/>.</summary>
public static class AccessFactKind
{
    /// <summary>The person holds the role named by the fact's value.</summary>
    public const string Role = "role";

    /// <summary>The person holds the capability term named by the fact's value (an agent grant).</summary>
    public const string Capability = "capability";

    /// <summary>The person holds the permission named by the fact's value, on the fact's resource.</summary>
    public const string Permission = "permission";

    /// <summary>An overwrite withholds the permission named by the fact's value on the fact's resource.</summary>
    public const string Deny = "deny";

    /// <summary>
    /// No role the person holds grants the permission named by the fact's value. No row backs it, so it cannot be
    /// revoked; it is the deciding fact when nothing denied the permission and nothing granted it.
    /// </summary>
    public const string Ungranted = "ungranted";
}

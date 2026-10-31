namespace Koan.Web.Hosting;

/// <summary>
/// WEB-0081: what a capability active in this host requires of an accepted URL, beyond what Koan.Web requires of every
/// origin. <see cref="KoanWebBaseUrl.Accept"/> refuses an origin a rule refuses before the store sees it, and a stored
/// one is not held at start. Koan resolves the rules at first use, so a rule may depend on what depends on the base URL.
/// </summary>
public interface IApplicationUrlRule
{
    /// <summary>
    /// Why this host could not use <paramref name="origin"/>, already normalised, worded to follow "It is 'origin':";
    /// null when it can.
    /// </summary>
    string? Refuses(string origin);
}

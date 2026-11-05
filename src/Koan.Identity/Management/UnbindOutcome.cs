namespace Koan.Identity.Management;

/// <summary>What unbinding a login from a person did.</summary>
public enum UnbindOutcome
{
    /// <summary>The login is no longer the person's; its next sign-in arrives as a new person.</summary>
    Unbound = 0,

    /// <summary>The person holds no such login.</summary>
    NotFound,

    /// <summary>The login is the person's only one, and stays: they bind another first.</summary>
    LastLogin,
}

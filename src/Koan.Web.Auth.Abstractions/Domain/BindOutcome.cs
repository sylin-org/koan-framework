namespace Koan.Web.Auth.Domain;

/// <summary>What binding a login to a person did.</summary>
public enum BindOutcome
{
    /// <summary>The login is the person's, in place of any other login of its provider they held.</summary>
    Bound = 0,

    /// <summary>The login is another person's, and stays theirs.</summary>
    AnotherPersons,

    /// <summary>The session acts as the person without being them, as an operator impersonating does: only they bind.</summary>
    NotThePerson,
}

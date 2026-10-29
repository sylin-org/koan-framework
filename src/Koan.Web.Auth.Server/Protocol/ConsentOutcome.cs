namespace Koan.Web.Auth.Server.Protocol;

/// <summary>SEC-0028 — what the person decided, as the done page reads it from <see cref="ConsentPage.Outcome"/>.</summary>
public enum ConsentOutcome
{
    /// <summary>The person approved the request; the device receives its token at its next poll.</summary>
    Approved,

    /// <summary>The request was denied; the device is answered <c>access_denied</c>.</summary>
    Denied,
}

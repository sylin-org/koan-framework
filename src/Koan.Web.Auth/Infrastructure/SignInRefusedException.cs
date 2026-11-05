using Microsoft.AspNetCore.Authentication;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// A callback that verified its login but refuses to sign it in, naming why in <see cref="SignInFailure"/>'s vocabulary.
/// It carries the challenge's properties, so the failure landing named there (WEB-0080) is found even where a handler
/// reports the failure without them.
/// </summary>
public sealed class SignInRefusedException(SignInFailure failure, AuthenticationProperties? properties, string message)
    : Exception(message)
{
    /// <summary>Why the sign-in was refused.</summary>
    public SignInFailure Failure { get; } = failure;

    /// <summary>The properties the challenge carried.</summary>
    public AuthenticationProperties? Properties { get; } = properties;
}

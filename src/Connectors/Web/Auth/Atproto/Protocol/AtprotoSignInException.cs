using Koan.Web.Auth.Infrastructure;

namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>
/// A sign-in that could not complete, classified in the Web Auth vocabulary (WEB-0080) at the phase that failed:
/// identity resolution, provider discovery, the callback's binding or the person's refusal. The message and the
/// cause stay server-side; the browser receives only <see cref="Failure"/>'s code.
/// </summary>
internal sealed class AtprotoSignInException(SignInFailure failure, string message, Exception? cause = null)
    : InvalidOperationException(message, cause)
{
    public SignInFailure Failure { get; } = failure;
}

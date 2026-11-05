using System.Text.Encodings.Web;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;
using Koan.Web.Auth.Connector.Atproto.Options;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Auth.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Connector.Atproto.Hosting;

internal sealed class AtprotoAuthenticationHandler(IOptionsMonitor<AtprotoAuthenticationOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, AtprotoSessions sessions)
    : RemoteAuthenticationHandler<AtprotoAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
      try
      {
        if (!sessions.Enabled) throw new InvalidOperationException("AT provider is inactive.");
        var identifier = properties.Items.TryGetValue(Constants.Account, out var account) && account is not null
            ? new Microsoft.Extensions.Primitives.StringValues(account) : Request.Query["identifier"];
        if (identifier.Count > 1 || (identifier.Count == 1 && (string.IsNullOrWhiteSpace(identifier) || identifier.ToString().Length > 2048)))
            throw new InvalidOperationException("Supply one account handle or DID as identifier.");
        GenerateCorrelationId(properties);
        var protectedProperties = Options.StateDataFormat.Protect(properties);
        var authorization = identifier.Count == 0
            ? await sessions.StartServer("https://bsky.social", properties, protectedProperties, Context.RequestAborted)
            : await sessions.Start(identifier.ToString().Trim(), properties, protectedProperties, Context.RequestAborted);
        Response.Redirect(authorization);
      }
      catch (Exception exception) when (exception is not OperationCanceledException || !Context.RequestAborted.IsCancellationRequested)
      {
        // Expected discovery/PAR failures must not disclose provider bodies or escape as HTTP 500. The person goes to
        // the application's failure landing with a sanitized code when one is named (WEB-0080), else to this page.
        var failure = exception is AtprotoSignInException known ? known.Failure : SignInFailure.Failed;
        if (exception is AtprotoClientIdentityException)
            Logger.LogWarning("AT sign-in could not start ({Failure}): {Correction}", SignInFailureReturn.Code(failure), exception.Message);
        else
            Logger.LogWarning("AT sign-in could not start ({Failure}; {FailureType}).",
                SignInFailureReturn.Code(failure), (exception.InnerException ?? exception).GetType().Name);
        if (SignInFailureReturn.Redirect(Context, properties, failure)) return;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        Response.Headers.CacheControl = "no-store";
        Response.ContentType = "text/html; charset=utf-8";
        await Response.WriteAsync("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Sign-in unavailable</title><h1>Sign-in could not be started</h1><p>Check your account handle and try again. Your provider may be unavailable or may not support the requested connection.</p><p><a href=\"" + ApplicationRoot() + "\">Return to sign-in</a></p></html>", Context.RequestAborted);
      }
    }

    protected override async Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
    {
        AuthenticationProperties? properties = null;
        try
        {
            if (!sessions.Enabled || Request.Query["state"].Count != 1) return Failed(SignInFailure.CallbackMismatch, properties);
            properties = Options.StateDataFormat.Unprotect(await sessions.BrowserProperties(Request.Query["state"].ToString(), Context.RequestAborted));
            if (properties is null || !ValidateCorrelationId(properties)) return Failed(SignInFailure.CallbackMismatch, properties);
            var principal = await sessions.Complete(Request.QueryString.ToString(), Context.RequestAborted);
            var did = principal.FindFirst(AtprotoClaimTypes.Did)!.Value;
            // SEC-0040: the verified login arrives as its person, or is bound to the signed-in person who asked.
            await LoginArrival.Arrive(Context, properties, AtprotoSessions.External(did, principal.FindFirst(AtprotoClaimTypes.Handle)?.Value));
            properties.Items.Remove(Constants.RequestedScopes);
            properties.Items.Remove(Constants.Account);
            properties.Items.Remove(AuthConstants.Items.FailurePath);
            return HandleRequestResult.Success(new AuthenticationTicket(principal, properties, Scheme.Name));
        }
        catch (AtprotoMissingScopesException)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Response.Headers.CacheControl = "no-store";
            Response.ContentType = "text/html; charset=utf-8";
            await Response.WriteAsync("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>More access is needed</title><h1>Your account could not connect this access</h1><p>Your account provider did not grant the access needed for this connection. Your previous sign-in has been kept.</p><p>Try an account whose provider supports the requested access, or check the connection settings.</p><p><a href=\"" + ApplicationRoot() + "\">Return to the app</a></p></html>", Context.RequestAborted);
            return HandleRequestResult.Handle();
        }
        catch (SignInRefusedException refused)
        {
            return Failed(refused.Failure, properties);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // SDK exceptions can include provider response bodies. Never reflect them into browser output/logs.
            return Failed(exception is AtprotoSignInException known ? known.Failure : SignInFailure.Failed, properties);
        }
    }

    /// <summary>
    /// The application's failure landing with the sanitized code when one is named (WEB-0080); otherwise the remote
    /// failure the module answers.
    /// </summary>
    private HandleRequestResult Failed(SignInFailure failure, AuthenticationProperties? properties)
    {
        if (SignInFailureReturn.Redirect(Context, properties, failure)) return HandleRequestResult.Handle();
        const string message = "AT authentication could not be verified; start sign-in again.";
        return properties is null ? HandleRequestResult.Fail(message) : HandleRequestResult.Fail(message, properties);
    }

    /// <summary>The application's root under the request's path base (WEB-0078), as an attribute value.</summary>
    private string ApplicationRoot() => HtmlEncoder.Default.Encode(Request.PathBase.HasValue ? Request.PathBase + "/" : "/");
}

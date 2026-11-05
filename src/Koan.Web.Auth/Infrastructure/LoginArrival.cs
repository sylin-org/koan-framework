using System.Security.Claims;
using Koan.Web.Auth.Domain;
using Koan.Web.Auth.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// SEC-0040 — where a provider's verified login arrives, for every protocol, before anything signs in. An ordinary
/// sign-in hands the login to <see cref="IExternalIdentityStore.Link"/>, which signs it in as the person it is bound to.
/// A binding challenge (<c>/me/connections/{provider}/link</c>) carried the person it binds to: that person must still be
/// the one signed in, and <see cref="IExternalIdentityStore.Bind"/> binds the login to them. The sign-in that follows then
/// arrives as that person with the new login's claims. A refusal throws <see cref="SignInRefusedException"/>, so nothing
/// signs in.
/// </summary>
public static class LoginArrival
{
    public static async Task Arrive(HttpContext context, AuthenticationProperties? properties, ExternalIdentity login)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(login);
        var store = context.RequestServices.GetService<IExternalIdentityStore>();
        if (properties is null || !properties.Items.Remove(AuthConstants.Items.Bind, out var person) || string.IsNullOrEmpty(person))
        {
            if (store is not null) await store.Link(login, context.RequestAborted);
            return;
        }

        if (store is null)
            throw new InvalidOperationException("Binding a login to a person needs a store that knows people; reference Koan.Identity.");
        var current = (await context.AuthenticateAsync(AuthenticationExtensions.CookieScheme)).Principal;
        var signedIn = current?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? current?.FindFirst("sub")?.Value;
        if (current is null || !string.Equals(signedIn, person, StringComparison.Ordinal))
            throw new SignInRefusedException(SignInFailure.SignInFirst, properties,
                "The person who asked to bind this login is no longer the one signed in.");
        switch (await store.Bind(current, login, context.RequestAborted))
        {
            case BindOutcome.AnotherPersons:
                throw new SignInRefusedException(SignInFailure.LoginTaken, properties, "This login is another person's, and stays theirs.");
            case BindOutcome.NotThePerson:
                throw new SignInRefusedException(SignInFailure.SignInFirst, properties, "Only the person themselves binds a login to them.");
        }
    }
}

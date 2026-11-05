using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Identity.Reconciliation;
using Koan.Web.Auth.Domain;

namespace Koan.Identity.Management;

/// <summary>
/// SEC-0040 — the logins a person holds, bound and unbound by the person themselves. A person holds at most one login per
/// provider and never fewer than one. Binding is authorized by the person's own session (the GitHub/Discord pattern) and
/// a login the provider just verified, never by a matching email, so a low-assurance or attacker IdP cannot acquire
/// someone else's account. Nothing merges people.
/// </summary>
public sealed class IdentityLinkService
{
    /// <summary>
    /// Binds the login (<paramref name="provider"/>, <paramref name="subject"/>) to <paramref name="personId"/>, in the
    /// place of the login of <paramref name="provider"/> they held, whose subject then arrives as a new person. A login
    /// that is another person's stays theirs.
    /// </summary>
    public async Task<BindOutcome> Bind(string personId, string provider, string subject, string? claimsJson = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(personId);
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentException.ThrowIfNullOrEmpty(subject);
        var keyHash = IdentityReconciler.ProviderKeyHash(subject);
        var id = ExternalIdentityLink.KeyFor(subject, provider, keyHash);
        var link = await ExternalIdentityLink.Get(id, ct).ConfigureAwait(false);
        if (link is null)
        {
            var written = await ExternalIdentityLink.Insert(new ExternalIdentityLink
            {
                Id = id,
                IdentityId = personId,
                Provider = provider,
                ProviderKeyHash = keyHash,
                ClaimsJson = claimsJson,
            }, ct: ct).ConfigureAwait(false);
            if (written.Outcome != MutationOutcome.Inserted)
                link = await ExternalIdentityLink.Get(id, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("A login written by a concurrent sign-in could not be read back; bind again.");
        }
        if (link is not null && !string.Equals(link.IdentityId, personId, StringComparison.Ordinal))
            return BindOutcome.AnotherPersons;

        foreach (var earlier in await ForPerson(personId, ct).ConfigureAwait(false))
            if (earlier.Id != id && string.Equals(earlier.Provider, provider, StringComparison.Ordinal))
                await earlier.Remove(ct).ConfigureAwait(false);
        return BindOutcome.Bound;
    }

    /// <summary>
    /// The logins <paramref name="personId"/> holds (the "connected accounts" view): at most one per provider, so as many
    /// as the providers they bound. A store that compares ids by its own collation answers more; only exact ones are kept.
    /// </summary>
    public async Task<IReadOnlyList<ExternalIdentityLink>> ForPerson(string personId, CancellationToken ct = default)
        => (await ExternalIdentityLink.Query(l => l.IdentityId == personId, ct).ConfigureAwait(false))
            .Where(l => string.Equals(l.IdentityId, personId, StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// Unbinds the login <paramref name="linkId"/> from <paramref name="personId"/>, whose own it must be, and never their
    /// last: a person signs in with a login. The unbound subject's next sign-in arrives as a new person.
    /// </summary>
    public async Task<UnbindOutcome> Unbind(string personId, string linkId, CancellationToken ct = default)
    {
        var logins = await ForPerson(personId, ct).ConfigureAwait(false);
        var link = logins.FirstOrDefault(l => string.Equals(l.Id, linkId, StringComparison.Ordinal));
        if (link is null) return UnbindOutcome.NotFound; // never someone else's
        if (logins.Count == 1) return UnbindOutcome.LastLogin;
        await link.Remove(ct).ConfigureAwait(false);
        return UnbindOutcome.Unbound;
    }
}

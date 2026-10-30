using Koan.Data.Core;
using Koan.Identity.Roles;
using Koan.Web.Auth.Standing;

namespace Koan.Identity.Infrastructure;

/// <summary>
/// The durable person's word on their standing: active only while their <see cref="Identity"/> row exists and is
/// active, with the name it holds and the roles their membership rows hold. A subject with no row was erased, or never
/// signed in, and may not act. Both reads go to the store, not to the compiled bag, because a process that did not
/// make the change has no way to know its bag is stale, and a refresh is rare enough to pay for the truth.
/// </summary>
internal sealed class IdentityStandingContributor(RoleCollection roles) : ISubjectStandingContributor
{
    public async Task<SubjectStanding?> Read(string subject, CancellationToken ct)
    {
        var person = await Identity.Get(subject, ct).ConfigureAwait(false);
        if (person is not { IsActive: true }) return new SubjectStanding(false);
        var held = await roles.ForPerson(subject, ct).ConfigureAwait(false);
        return new SubjectStanding(true, person.DisplayName, held.Select(static role => role.Key).ToArray());
    }
}

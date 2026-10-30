using Koan.Core;

namespace Koan.Web.Auth.Standing;

/// <summary>
/// One owner's word on a subject's standing. Auto-discovered via <see cref="KoanDiscoverableAttribute"/> and registered as
/// a scoped service, so an application contributes by implementing this: a place that bans someone answers
/// <c>new SubjectStanding(Active: false)</c> while the ban stands. A contributor that fails fails the question, and
/// nothing is minted on it. Contributors compose without order: any that says inactive decides, and the roles of all
/// are held together.
/// </summary>
[KoanDiscoverable]
public interface ISubjectStandingContributor
{
    /// <summary>What this owner knows of <paramref name="subject"/> now, or null when it knows nothing of them.</summary>
    Task<SubjectStanding?> Read(string subject, CancellationToken ct);
}

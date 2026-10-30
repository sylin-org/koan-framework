using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Identity.Access;

namespace Koan.Identity.Roles;

/// <summary>
/// What one role, or one person, may and may not do on one resource beyond what their roles grant everywhere: the
/// <c>global:*</c> permissions it allows and denies there. The resource id is the application's and opaque to
/// Identity. One row per resource and subject, whose id is composed from both (<see cref="KeyFor"/>): a person's own
/// overwrite on a resource is one read by that key. A resource's overwrites are read by resource, kind and subject, and
/// a subject's across resources by kind, subject and resource. <see cref="RoleCollection"/> is its only mutation owner.
/// </summary>
public sealed class PermissionOverwrite : Entity<PermissionOverwrite>, IAmbientExempt, IRequiresLifecycleEnforcement
{
    private const string ResourceIndex = "ix_permission_overwrites_resource_subject";
    private const string SubjectIndex = "ix_permission_overwrites_subject_resource";

    /// <summary>The application's resource id: a channel, a board, a project, a folder.</summary>
    [Index(Group = ResourceIndex, Order = 0)]
    [Index(Group = SubjectIndex, Order = 2)]
    public string ResourceId { get; set; } = "";

    /// <summary>Whether <see cref="SubjectId"/> is a role key or a person id.</summary>
    [Index(Group = ResourceIndex, Order = 1)]
    [Index(Group = SubjectIndex, Order = 0)]
    public OverwriteSubjectKind SubjectKind { get; set; }

    /// <summary>The role key — <see cref="RoleTokens.Everyone"/> for everyone — or the person id.</summary>
    [Index(Group = ResourceIndex, Order = 2)]
    [Index(Group = SubjectIndex, Order = 1)]
    public string SubjectId { get; set; } = "";

    /// <summary>The permissions granted on the resource.</summary>
    public List<string> Allow { get; set; } = [];

    /// <summary>The permissions withheld on the resource.</summary>
    public List<string> Deny { get; set; } = [];

    /// <summary>When the overwrite stops applying; <see langword="null"/> applies until it is removed.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    [Timestamp] public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>The one id of the overwrite of <paramref name="subjectId"/> on <paramref name="resourceId"/>.</summary>
    public static string KeyFor(string resourceId, OverwriteSubjectKind subjectKind, string subjectId)
        => RoleContract.ComposeId(nameof(PermissionOverwrite), resourceId, subjectKind.ToString(), subjectId);

    /// <summary>Whether the overwrite applies at <paramref name="now"/>.</summary>
    public bool AppliesAt(DateTimeOffset now) => ExpiresAt is not { } expires || expires > now;

    internal AccessFact Fact(string kind, string permission) => new(
        nameof(PermissionOverwrite), kind, permission, ResourceId, AccessFactScope.Global,
        nameof(PermissionOverwrite), Id, ExpiresAt, SubjectId);

    internal string Describe() => SubjectKind == OverwriteSubjectKind.Person
        ? $"person '{SubjectId}'"
        : SubjectId == RoleTokens.Everyone ? RoleTokens.Everyone : $"role '{SubjectId}'";

    internal PermissionOverwrite Duplicate() => new()
    {
        Id = Id, ResourceId = ResourceId, SubjectKind = SubjectKind, SubjectId = SubjectId, Allow = [.. Allow],
        Deny = [.. Deny], ExpiresAt = ExpiresAt, UpdatedAt = UpdatedAt, UpdatedBy = UpdatedBy,
    };
}

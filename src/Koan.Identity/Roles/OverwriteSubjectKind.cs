namespace Koan.Identity.Roles;

/// <summary>What a <see cref="PermissionOverwrite"/>'s subject is.</summary>
public enum OverwriteSubjectKind
{
    /// <summary>A role key; <see cref="RoleTokens.Everyone"/> is the overwrite for everyone.</summary>
    Role,

    /// <summary>A person id.</summary>
    Person,
}

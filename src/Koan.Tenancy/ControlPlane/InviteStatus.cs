namespace Koan.Tenancy;

/// <summary>The status of an <see cref="Invite"/> (ARCH-0099 §2). <see cref="Pending"/> is the default (value 0).</summary>
public enum InviteStatus
{
    /// <summary>Issued and awaiting acceptance.</summary>
    Pending = 0,

    /// <summary>Accepted — a <see cref="Membership"/> was created for the accepting identity.</summary>
    Accepted = 1,

    /// <summary>Revoked by an owner/admin before acceptance.</summary>
    Revoked = 2,

    /// <summary>Expired past its window without acceptance.</summary>
    Expired = 3,
}

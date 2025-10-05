using System;

namespace Koan.Canon.Domain.Model;

/// <summary>
/// Legacy lifecycle enumeration maintained for temporary compatibility. Prefer <see cref="CanonLifecycle"/>.
/// </summary>
[Obsolete("Use CanonLifecycle instead. CanonStatus will be removed in a future build.")]
public enum CanonStatus
{
    /// <inheritdoc cref="CanonLifecycle.Active"/>
    Active = (int)CanonLifecycle.Active,

    /// <inheritdoc cref="CanonLifecycle.PendingRetirement"/>
    PendingRetirement = (int)CanonLifecycle.PendingRetirement,

    /// <inheritdoc cref="CanonLifecycle.Superseded"/>
    Superseded = (int)CanonLifecycle.Superseded,

    /// <inheritdoc cref="CanonLifecycle.Archived"/>
    Archived = (int)CanonLifecycle.Archived,

    /// <inheritdoc cref="CanonLifecycle.Withdrawn"/>
    Withdrawn = (int)CanonLifecycle.Withdrawn
}

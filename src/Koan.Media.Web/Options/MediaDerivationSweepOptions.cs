namespace Koan.Media.Web.Options;

/// <summary>
/// Configuration for the
/// <see cref="Sweep.MediaDerivationSweepService"/>. Bound from
/// <c>Koan:Media:Web:DerivationSweep</c>.
///
/// <para>The sweep is the MEDIA-0007 replacement for the never-evicted disk
/// cache: a scheduled task queries derivation rows
/// (<c>SourceMediaId != null</c>) and deletes those whose source is gone.
/// Disabled by default until hosts opt in.</para>
/// </summary>
public sealed class MediaDerivationSweepOptions
{
    /// <summary>Enable the scheduled orphan-derivation sweep. Default <c>false</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Cadence between sweep runs. Defaults to one hour. The first run fires
    /// after <see cref="InitialDelay"/> from host start so the app warms up
    /// before background work kicks in.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Delay before the first sweep run. Defaults to five minutes; set to
    /// <see cref="TimeSpan.Zero"/> to run immediately on startup.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(5);
}

namespace Koan.ZenGarden;

public sealed class ZenGardenWatchOptions
{
    /// <summary>
    /// Emit a synthetic initial event from current catalog state after subscription registration.
    /// </summary>
    public bool EmitInitialState { get; set; } = true;
}

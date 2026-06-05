namespace Koan.Jobs.Transport.Messaging;

/// <summary>
/// A lightweight cross-node wake — "a job is ready somewhere". It carries no work (the ledger is the truth),
/// only the originating node id so a publisher ignores its own echo.
/// </summary>
public sealed class JobReadySignal
{
    public string OriginNode { get; set; } = "";
}

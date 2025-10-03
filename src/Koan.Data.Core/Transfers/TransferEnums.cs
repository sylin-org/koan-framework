namespace Koan.Data.Core.Transfers;

public enum TransferKind
{
    Copy,
    Move,
    Mirror
}

public enum DeleteStrategy
{
    AfterCopy,
    Batched,
    Synced
}

public enum MirrorMode
{
    Push,
    Pull,
    Bidirectional
}

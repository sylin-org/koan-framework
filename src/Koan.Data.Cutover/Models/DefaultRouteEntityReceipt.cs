namespace Koan.Data.Cutover;

public sealed record DefaultRouteEntityReceipt(
    string RootIdentity,
    long Count,
    string Digest,
    TimeSpan Duration);

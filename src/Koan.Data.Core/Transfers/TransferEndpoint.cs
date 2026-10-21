namespace Koan.Data.Core.Transfers;

/// <summary>
/// Where one side of a transfer landed once its routing axes were resolved (NK-252): the data source, the provider
/// elected for it, and the container (table, collection, file) that provider names for the Entity and partition. The
/// declared <see cref="TransferContextSnapshot"/> says what the caller wrote; this says what the transfer used.
/// </summary>
public sealed record TransferEndpoint(string Source, string Provider, string Container);

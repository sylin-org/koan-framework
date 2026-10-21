using Koan.Data.Abstractions;

namespace Koan.Data.Core.Transfers;

/// <summary>What two endpoints' store markers say about them (DATA-0148).</summary>
internal enum StoreKinship
{
    /// <summary>No marker is readable on at least one side; the caller falls back to its other proofs.</summary>
    Unknown,
    /// <summary>Both read the same marker: one store.</summary>
    Same,
    /// <summary>Both read a marker and the markers differ: two stores.</summary>
    Different
}

/// <summary>
/// Asks two endpoints for their store markers, creating each where the source's DDL consent allows. Move (DATA-0143)
/// and cutover's physical-distinctness check ask this one question.
/// </summary>
internal static class StoreMarkers
{
    public static async ValueTask<StoreKinship> Compare(
        IDataAdapterFactory first,
        string firstSource,
        IDataAdapterFactory second,
        string secondSource,
        IServiceProvider services,
        CancellationToken ct)
    {
        var a = await Read(first, firstSource, create: true, services, ct).ConfigureAwait(false);
        var b = await Read(second, secondSource, create: true, services, ct).ConfigureAwait(false);
        // Only the second side may have been allowed to write the marker; if both are one store, the first reads it now.
        if (a is null && b is not null)
            a = await Read(first, firstSource, create: false, services, ct).ConfigureAwait(false);
        if (a is null || b is null) return StoreKinship.Unknown;
        return string.Equals(a, b, StringComparison.Ordinal) ? StoreKinship.Same : StoreKinship.Different;
    }

    private static async ValueTask<string?> Read(
        IDataAdapterFactory factory,
        string source,
        bool create,
        IServiceProvider services,
        CancellationToken ct)
    {
        try
        {
            var marker = await factory.ReadStoreMarker(services, source, create, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(marker) ? null : marker.Trim();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // An unreadable marker proves nothing; the caller falls back to the proofs it had before markers.
            return null;
        }
    }
}

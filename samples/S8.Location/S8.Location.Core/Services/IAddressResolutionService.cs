namespace S8.Location.Core.Services;

public interface IAddressResolutionService
{
    /// <summary>
    /// Resolves an address to its canonical AgnosticLocation ID.
    /// Note: the Canon pipeline handles deduplication via AddressHash AggregationKey.
    /// This service is only called for NEW addresses that need resolution.
    /// </summary>
    Task<string> ResolveToCanonicalIdAsync(string address, CancellationToken ct = default);
    
    /// <summary>
    /// Normalizes an address for consistent hashing
    /// </summary>
    string NormalizeAddress(string address);
    
    /// <summary>
    /// Computes SHA512 hash of input string
    /// </summary>
    string ComputeSHA512(string input);
}
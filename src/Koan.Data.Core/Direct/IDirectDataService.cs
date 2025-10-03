namespace Koan.Data.Core.Direct;

public interface IDirectDataService
{
    /// <summary>
    /// Creates a direct session for ad-hoc SQL commands.
    /// Specify either source OR adapter, not both (source XOR adapter constraint).
    /// </summary>
    IDirectSession Direct(string? source = null, string? adapter = null);
}
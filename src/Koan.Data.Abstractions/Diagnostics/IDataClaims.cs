using Koan.Core.Capabilities;

namespace Koan.Data.Abstractions;

/// <summary>Minimal declaration surface through which an adapter publishes executable Data claims.</summary>
public interface IDataClaims
{
    IDataClaims Profile(string profile, string? qualifier = null, bool advertised = true);
    IDataClaims Capability(Capability capability, bool advertised = true);
}

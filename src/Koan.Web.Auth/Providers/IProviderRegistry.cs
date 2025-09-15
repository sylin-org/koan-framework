using Koan.Web.Auth.Options;

namespace Koan.Web.Auth.Providers;

public interface IProviderRegistry
{
    IReadOnlyDictionary<string, ProviderOptions> EffectiveProviders { get; }
    IEnumerable<ProviderDescriptor> GetDescriptors();
}
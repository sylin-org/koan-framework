using Sora.Web.Auth.Options;

namespace Sora.Web.Auth.Providers;

public interface IProviderRegistry
{
    IReadOnlyDictionary<string, ProviderOptions> EffectiveProviders { get; }
    IEnumerable<ProviderDescriptor> GetDescriptors();
}
using Sora.Web.Auth.Options;

namespace Sora.Web.Auth.Providers;

public interface IAuthProviderContributor
{
    // Return additional provider defaults keyed by id. Do not read configuration here; stick to static defaults.
    IReadOnlyDictionary<string, ProviderOptions> GetDefaults();
}
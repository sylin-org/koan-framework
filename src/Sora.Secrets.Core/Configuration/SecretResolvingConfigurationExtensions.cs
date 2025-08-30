using Microsoft.Extensions.Configuration;

namespace Sora.Secrets.Core.Configuration;

public static class SecretResolvingConfigurationExtensions
{
    public static IConfigurationBuilder AddSecretsReferenceConfiguration(this IConfigurationBuilder builder, IServiceProvider? serviceProvider = null)
    {
        var cfg = builder.Build();
        builder.Add(new SecretResolvingConfigurationSource(serviceProvider, cfg));
        return builder;
    }

    /// <summary>
    /// Upgrades any active SecretResolvingConfiguration providers from the bootstrap resolver to the DI-backed resolver
    /// and triggers configuration reload tokens so options rebind.
    /// </summary>
    public static void UpgradeSecretsConfiguration(IServiceProvider serviceProvider)
    {
        // delegate to the nested provider registry
        var providerType = typeof(SecretResolvingConfigurationSource)
            .GetNestedType("Provider", System.Reflection.BindingFlags.NonPublic);
        var mi = providerType?.GetMethod("UpgradeAll", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
        mi?.Invoke(null, new object?[] { serviceProvider });
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web.Auth.Extensions;
using Koan.Web.Extensions;
using Koan.Web.Auth.Pillars;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Web.Auth.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Auth";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        SecurityPillarManifest.EnsureRegistered();
        // Ensure auth services are registered once
        services.AddKoanWebAuth();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.KoanWebAuthStartupFilter>());

        // Ensure MVC discovers controllers from this assembly
        services.AddKoanControllersFrom<Controllers.DiscoveryController>();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        cfg ??= new ConfigurationBuilder().Build();

        // Best-effort discovery summary without binding or DI: list provider display names and protocol
        // Strategy: if configured providers exist, list those; otherwise fall back to well-known defaults.
        var section = cfg.GetSection(Options.AuthOptions.SectionPath);
        var providers = section.GetSection("Providers");

        var configured = LoadConfiguredProviders(providers);
        var defaults = DiscoverContributorProviders(cfg, env);
        var effective = ComposeEffectiveProviders(configured, defaults);

        var detected = effective.Select(pair => FormatProvider(pair.Key, pair.Value)).ToList();

        var providerSectionKey = $"{Options.AuthOptions.SectionPath}:{nameof(Options.AuthOptions.Providers)}";
        var providerSource = configured.Count > 0
            ? Koan.Core.Hosting.Bootstrap.BootSettingSource.AppSettings
            : defaults.Count > 0
                ? Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto
                : Koan.Core.Hosting.Bootstrap.BootSettingSource.Auto;

        module.AddSetting(
            "Providers",
            effective.Count.ToString(),
            source: providerSource,
            consumers: new[] { "Koan.Web.Auth.ProviderRegistry" },
            sourceKey: providerSectionKey);

        module.AddSetting(
            "DetectedProviders",
            detected.Count == 0 ? "(none)" : string.Join(", ", detected),
            source: providerSource,
            consumers: new[] { "Koan.Web.Auth.ProviderRegistry" },
            sourceKey: providerSectionKey);

        // Production gating for dynamic providers (adapter/contributor defaults without explicit config)
        var allowDynamicOption = Koan.Core.Configuration.ReadWithSource(
            cfg,
            Infrastructure.AuthConstants.Configuration.AllowDynamicProvidersInProduction,
            false);
        var allowMagicOption = Koan.Core.Configuration.ReadWithSource(
            cfg,
            Koan.Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction,
            false);

        var allowDynamic = allowDynamicOption.Value
                           || Koan.Core.KoanEnv.AllowMagicInProduction
                           || allowMagicOption.Value;

        var dynamicSource = allowDynamicOption.Value
            ? allowDynamicOption.Source
            : allowMagicOption.Value
                ? allowMagicOption.Source
                : Koan.Core.KoanEnv.AllowMagicInProduction
                    ? Koan.Core.Hosting.Bootstrap.BootSettingSource.Environment
                    : allowDynamicOption.Source;
        var dynamicSourceKey = !allowDynamicOption.UsedDefault
            ? allowDynamicOption.ResolvedKey
            : !allowMagicOption.UsedDefault
                ? allowMagicOption.ResolvedKey
                : Koan.Core.Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction;

        if (Koan.Core.KoanEnv.IsProduction && !allowDynamic)
        {
            module.AddSetting(
                "DynamicProvidersInProduction",
                "disabled (set Koan:Web:Auth:AllowDynamicProvidersInProduction=true or Koan:AllowMagicInProduction=true)",
                source: dynamicSource,
                consumers: new[] { "Koan.Web.Auth.ProviderRegistry" },
                sourceKey: dynamicSourceKey);
        }
        else
        {
            module.AddSetting(
                "DynamicProvidersInProduction",
                "enabled",
                source: dynamicSource,
                consumers: new[] { "Koan.Web.Auth.ProviderRegistry" },
                sourceKey: dynamicSourceKey);
        }

        module.AddTool(
            "Auth Provider Discovery",
            Infrastructure.AuthConstants.Routes.Discovery,
            "Lists configured authentication providers",
            capability: "auth.discovery");
    }

    private static Dictionary<string, Options.ProviderOptions> LoadConfiguredProviders(IConfigurationSection providers)
    {
        var result = new Dictionary<string, Options.ProviderOptions>(StringComparer.OrdinalIgnoreCase);
        if (providers is null || !providers.Exists()) return result;

        foreach (var child in providers.GetChildren())
        {
            var options = new Options.ProviderOptions();
            child.Bind(options);
            result[child.Key] = options;
        }

        return result;
    }

    private static Dictionary<string, Options.ProviderOptions> DiscoverContributorProviders(IConfiguration cfg, IHostEnvironment env)
    {
        var result = new Dictionary<string, Options.ProviderOptions>(StringComparer.OrdinalIgnoreCase);
        var contract = typeof(Providers.IAuthProviderContributor);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                if (!contract.IsAssignableFrom(type) || type.IsInterface || type.IsAbstract) continue;
                var contributor = CreateContributor(type, cfg, env);
                if (contributor is null) continue;

                try
                {
                    var defaults = contributor.GetDefaults();
                    if (defaults is null) continue;
                    foreach (var kv in defaults)
                    {
                        result[kv.Key] = kv.Value;
                    }
                }
                catch
                {
                    // ignore contributor failures during boot reporting
                }
            }
        }

        return result;
    }

    private static Providers.IAuthProviderContributor? CreateContributor(Type type, IConfiguration cfg, IHostEnvironment env)
    {
        try
        {
            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor is not null)
            {
                return (Providers.IAuthProviderContributor?)Activator.CreateInstance(type);
            }

            ctor = type.GetConstructor(new[] { typeof(IConfiguration), typeof(IHostEnvironment) });
            if (ctor is not null)
            {
                return (Providers.IAuthProviderContributor?)ctor.Invoke(new object?[] { cfg, env });
            }

            ctor = type.GetConstructor(new[] { typeof(IConfiguration) });
            if (ctor is not null)
            {
                return (Providers.IAuthProviderContributor?)ctor.Invoke(new object?[] { cfg });
            }

            ctor = type.GetConstructor(new[] { typeof(IHostEnvironment) });
            if (ctor is not null)
            {
                return (Providers.IAuthProviderContributor?)ctor.Invoke(new object?[] { env });
            }
        }
        catch
        {
            // ignore creation failure
        }

        return null;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Select(t => t!);
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static Dictionary<string, Options.ProviderOptions> ComposeEffectiveProviders(
        IDictionary<string, Options.ProviderOptions> configured,
        IDictionary<string, Options.ProviderOptions> defaults)
    {
        var result = new Dictionary<string, Options.ProviderOptions>(defaults, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in configured)
        {
            if (result.TryGetValue(pair.Key, out var existing))
            {
                result[pair.Key] = MergeProviders(existing, pair.Value);
            }
            else
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    private static Options.ProviderOptions MergeProviders(Options.ProviderOptions baseline, Options.ProviderOptions overlay)
    {
        return new Options.ProviderOptions
        {
            Type = overlay.Type ?? baseline.Type,
            DisplayName = overlay.DisplayName ?? baseline.DisplayName,
            Icon = overlay.Icon ?? baseline.Icon,
            Enabled = overlay.Enabled && baseline.Enabled,
            Priority = overlay.Priority ?? baseline.Priority,
            Authority = overlay.Authority ?? baseline.Authority,
            ClientId = overlay.ClientId ?? baseline.ClientId,
            ClientSecret = overlay.ClientSecret ?? baseline.ClientSecret,
            SecretRef = overlay.SecretRef ?? baseline.SecretRef,
            Scopes = overlay.Scopes ?? baseline.Scopes,
            CallbackPath = overlay.CallbackPath ?? baseline.CallbackPath,
            AuthorizationEndpoint = overlay.AuthorizationEndpoint ?? baseline.AuthorizationEndpoint,
            TokenEndpoint = overlay.TokenEndpoint ?? baseline.TokenEndpoint,
            UserInfoEndpoint = overlay.UserInfoEndpoint ?? baseline.UserInfoEndpoint,
            EntityId = overlay.EntityId ?? baseline.EntityId,
            IdpMetadataUrl = overlay.IdpMetadataUrl ?? baseline.IdpMetadataUrl,
            IdpMetadataXml = overlay.IdpMetadataXml ?? baseline.IdpMetadataXml,
            SigningCertRef = overlay.SigningCertRef ?? baseline.SigningCertRef,
            DecryptionCertRef = overlay.DecryptionCertRef ?? baseline.DecryptionCertRef,
            AllowIdpInitiated = overlay.AllowIdpInitiated || baseline.AllowIdpInitiated,
            ClockSkewSeconds = overlay.ClockSkewSeconds != 120 ? overlay.ClockSkewSeconds : baseline.ClockSkewSeconds
        };
    }

    private static string FormatProvider(string id, Options.ProviderOptions options)
    {
        var name = string.IsNullOrWhiteSpace(options.DisplayName) ? Titleize(id) : options.DisplayName!;
        var protocol = PrettyProtocol(options.Type);
        return options.Enabled ? $"{name} ({protocol})" : $"{name} ({protocol}, disabled)";
    }

    private static string PrettyProtocol(string? type)
        => string.IsNullOrWhiteSpace(type) ? "OIDC"
           : type!.ToLowerInvariant() switch
           {
               "oidc" => "OIDC",
               "oauth2" => "OAuth",
               "oauth" => "OAuth",
               "saml" => "SAML",
               "ldap" => "LDAP",
               _ => type
           };

    private static string Titleize(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return id;
        var parts = id.Replace('-', ' ').Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            parts[i] = char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p.Substring(1) : string.Empty);
        }
        return string.Join(' ', parts);
    }
}


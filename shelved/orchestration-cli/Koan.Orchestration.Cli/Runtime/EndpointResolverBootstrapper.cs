using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Orchestration.Attributes;
using Koan.Orchestration.Cli.Formatting;

namespace Koan.Orchestration.Cli.Runtime;

internal static class EndpointResolverBootstrapper
{
    private static bool _initialized;
    private static readonly object Sync = new();

    public static void Register()
    {
        if (_initialized) return;
        lock (Sync)
        {
            if (_initialized) return;
            try
            {
                var map = new Dictionary<(string Prefix, int Port), (string Scheme, string? Pattern)>();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t is null) continue;
                        var attrs = t.GetCustomAttributes(typeof(DefaultEndpointAttribute), inherit: false).Cast<DefaultEndpointAttribute>();
                        foreach (var attr in attrs)
                        {
                            if (attr.ImagePrefixes is null || attr.ImagePrefixes.Length == 0) continue;
                            foreach (var prefix in attr.ImagePrefixes)
                            {
                                if (string.IsNullOrWhiteSpace(prefix)) continue;
                                map[(prefix, attr.ContainerPort)] = (attr.Scheme, attr.UriPattern);
                            }
                        }
                    }
                }

                EndpointFormatter.UseEndpointResolver((serviceIdOrImage, containerPort) =>
                {
                    if (!string.IsNullOrWhiteSpace(serviceIdOrImage))
                    {
                        foreach (var kv in map)
                        {
                            if (serviceIdOrImage.StartsWith(kv.Key.Prefix, StringComparison.OrdinalIgnoreCase)
                                && kv.Key.Port == containerPort)
                            {
                                return kv.Value;
                            }
                        }
                    }
                    return ("tcp", null);
                });
                EndpointFormatter.UseSchemeResolver((serviceIdOrImage, containerPort) =>
                {
                    if (!string.IsNullOrWhiteSpace(serviceIdOrImage))
                    {
                        foreach (var kv in map)
                        {
                            if (serviceIdOrImage.StartsWith(kv.Key.Prefix, StringComparison.OrdinalIgnoreCase)
                                && kv.Key.Port == containerPort)
                            {
                                return kv.Value.Scheme;
                            }
                        }
                    }
                    return "tcp";
                });
            }
            catch
            {
                // fallback to EndpointFormatter defaults
            }
            _initialized = true;
        }
    }
}

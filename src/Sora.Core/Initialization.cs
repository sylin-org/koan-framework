using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sora.Core;

// Modules (e.g., data adapters) implement this to self-register services/options.
public interface ISoraInitializer
{
    void Initialize(IServiceCollection services);
}

public static class SoraInitialization
{
    public sealed class SoraOptions
    {
        // Discovery always runs as per requirement, but we can warn in Production.
        public bool WarnDiscoveryInProduction { get; set; } = true;
    }

    public static void InitializeModules(IServiceCollection services)
    {
        // Ensure options are present (can be configured by host)
        services.AddOptions<SoraOptions>();

        // Build a closure of loaded + referenced assemblies
        var set = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        void AddAsm(Assembly a)
        {
            var name = a.GetName().Name ?? string.Empty;
            if (!set.ContainsKey(name)) set[name] = a;
        }

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) AddAsm(a);
        if (Assembly.GetEntryAssembly() is { } entry) AddAsm(entry);
        if (Assembly.GetExecutingAssembly() is { } exec) AddAsm(exec);

        var changed = true; var guard = 0;
        while (changed && guard++ < 5)
        {
            changed = false;
            var current = set.Values.ToList();
            foreach (var asm in current)
            {
                AssemblyName[] refs;
                try { refs = asm.GetReferencedAssemblies(); } catch { continue; }
                foreach (var rn in refs)
                {
                    if (set.ContainsKey(rn.Name!)) continue;
                    try { var loaded = Assembly.Load(rn); AddAsm(loaded); changed = true; }
                    catch { /* skip */ }
                }
            }
        }

        // Also load any Sora.*.dll assemblies from base directory
        try
        {
            var baseDir = AppContext.BaseDirectory;
            foreach (var file in System.IO.Directory.GetFiles(baseDir, "Sora.*.dll"))
            {
                try
                {
                    var asmName = AssemblyName.GetAssemblyName(file);
                    if (!set.ContainsKey(asmName.Name!))
                    {
                        var asm = Assembly.LoadFrom(file);
                        AddAsm(asm);
                    }
                }
                catch { /* ignore bad files */ }
            }
        }
        catch { /* ignore */ }

        foreach (var asm in set.Values)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }
            foreach (var t in types)
            {
                if (t.IsAbstract || !typeof(ISoraInitializer).IsAssignableFrom(t)) continue;
                try
                {
                    if (Activator.CreateInstance(t) is ISoraInitializer init)
                        init.Initialize(services);
                }
                catch { /* best effort */ }
            }
        }

    // Warning is logged at runtime in ISoraRuntime.Discover() to avoid building a temporary provider here.
    }
}

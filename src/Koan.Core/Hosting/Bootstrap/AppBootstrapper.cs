using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using System.Reflection;

namespace Koan.Core.Hosting.Bootstrap;

// Greenfield bootstrapper: wires up IKoanInitializer instances already registered or discoverable via DI.
public static class AppBootstrapper
{
    public static void InitializeModules(IServiceCollection services)
    {
        // Build a closure of loaded + referenced assemblies and populate AssemblyCache
        var set = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        var cache = AssemblyCache.Instance;

        void AddAsm(Assembly a)
        {
            var name = a.GetName().Name ?? string.Empty;
            if (!set.ContainsKey(name))
            {
                set[name] = a;
                cache.AddAssembly(a); // Cache for reuse by other components
            }
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

        // Also load any Koan.*.dll assemblies from base directory
        try
        {
            var baseDir = AppContext.BaseDirectory;
            foreach (var file in Directory.GetFiles(baseDir, "Koan.*.dll"))
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

        // CORE-0003: Always run initializers for every ServiceCollection.
        // Initializers are responsible for their own idempotency (AppDomain-scoped guards for static state).
        // This ensures "Reference = Intent" works in all scenarios (tests, multi-tenant, etc.).
        foreach (var asm in set.Values)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }
            foreach (var t in types)
            {
                if (t.IsAbstract || !typeof(IKoanInitializer).IsAssignableFrom(t)) continue;

                try
                {
                    if (Activator.CreateInstance(t) is IKoanInitializer init)
                        init.Initialize(services);
                }
                catch { /* best effort */ }
            }
        }
    }
}

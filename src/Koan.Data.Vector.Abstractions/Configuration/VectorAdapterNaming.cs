using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core.Routing;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector.Abstractions.Configuration;

/// <summary>
/// Resolves vector collection/index identifiers via the shared <see cref="StorageNameGenerator"/>, folding BOTH AODB
/// isolation discriminators into the name (the name-mangling floor every vector store realizes uniformly): the ambient
/// <b>partition</b> (Container mode) and the Database-mode routed <b>source</b> (ARCH-0103 §6). A distinct partition
/// resolves to a distinct physical collection (Container); a distinct routed source resolves to a distinct physical
/// collection on the same store (Database). Both off (no partition, source = Default) ⇒ byte-identical to the prior name.
///
/// <para>The factory still owns the naming <b>capability</b> (charset / separators / limit) via
/// <see cref="INamingProvider.GetNamingCapability"/>; this routes through the source-aware
/// <see cref="StorageNameGenerator.Resolve(string,Type,string?,string?,Func{StorageNamingCapability})"/> overload so the
/// source is folded with the SAME identifier-safe rendering as the partition (the record-plane <c>ResolveStorage</c>
/// stays source-free — the record plane routes a source to a distinct physical store via the factory, not the name).</para>
/// </summary>
public static class VectorAdapterNaming
{
    public static string GetOrCompute<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var cfg = VectorConfigs.Get<TEntity, TKey>(sp);
        var factory = sp.GetServices<IVectorAdapterFactory>().FirstOrDefault(f => f.CanHandle(cfg.Provider))
            ?? throw new InvalidOperationException($"No vector adapter factory for provider '{cfg.Provider}'.");
        // ARCH-0103: the SAME routed source the record plane + VectorService resolve (explicit EntityContext.Source >
        // Database-mode axis route > null). Folded into the name so the vector plane physically isolates per source.
        var source = RoutedSource.Resolve<TEntity>().Source;
        return StorageNameGenerator.Resolve(
            factory.Provider, typeof(TEntity), Koan.Data.Core.EntityContext.Current?.Partition, source,
            () => factory.GetNamingCapability(sp));
    }
}

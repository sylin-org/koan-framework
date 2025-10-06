using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Model;

public abstract partial class Entity<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public static EntityCacheAccessor Cache { get; } = new();

    public sealed class EntityCacheAccessor
    {
        internal EntityCacheAccessor()
        {
        }

        public ValueTask<long> Flush(CancellationToken ct = default)
            => FlushInternal(Array.Empty<string>(), ct);

        public ValueTask<long> Flush(IEnumerable<string> tags, CancellationToken ct = default)
            => FlushInternal(tags, ct);

        public ValueTask<long> Flush(string tag, CancellationToken ct = default)
            => FlushInternal(new[] { tag }, ct);

        public ValueTask<long> Count(CancellationToken ct = default)
            => CountInternal(Array.Empty<string>(), ct);

        public ValueTask<long> Count(IEnumerable<string> tags, CancellationToken ct = default)
            => CountInternal(tags, ct);

        public ValueTask<long> Count(string tag, CancellationToken ct = default)
            => CountInternal(new[] { tag }, ct);

        public async ValueTask<bool> Any(CancellationToken ct = default)
            => await CountInternal(Array.Empty<string>(), ct).ConfigureAwait(false) > 0;

        public async ValueTask<bool> Any(IEnumerable<string> tags, CancellationToken ct = default)
            => await CountInternal(tags, ct).ConfigureAwait(false) > 0;

        private static ValueTask<long> FlushInternal(IEnumerable<string>? tags, CancellationToken ct)
        {
            var client = ResolveClient();
            var resolved = ResolveTags(tags);
            if (resolved.Count == 0)
            {
                return ValueTask.FromResult(0L);
            }

            return client.FlushTagsAsync(resolved, ct);
        }

        private static ValueTask<long> CountInternal(IEnumerable<string>? tags, CancellationToken ct)
        {
            var client = ResolveClient();
            var resolved = ResolveTags(tags);
            if (resolved.Count == 0)
            {
                return ValueTask.FromResult(0L);
            }

            return client.CountTagsAsync(resolved, ct);
        }

        private static ICacheClient ResolveClient()
        {
            if (AppHost.Current?.GetService(typeof(ICacheClient)) is not ICacheClient client)
            {
                throw new InvalidOperationException("ICacheClient is not available. Ensure AddKoanCache() has been invoked.");
            }

            return client;
        }

        private static ICachePolicyRegistry ResolveRegistry()
        {
            if (AppHost.Current?.GetService(typeof(ICachePolicyRegistry)) is not ICachePolicyRegistry registry)
            {
                throw new InvalidOperationException("ICachePolicyRegistry is not available. Ensure AddKoanCache() has been invoked.");
            }

            return registry;
        }

        private static IReadOnlyCollection<string> ResolveTags(IEnumerable<string>? additionalTags)
        {
            var registry = ResolveRegistry();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var descriptor in registry.GetPoliciesFor(typeof(TEntity)))
            {
                if (descriptor.Scope is not (CacheScope.Entity or CacheScope.EntityQuery))
                {
                    continue;
                }

                foreach (var tag in descriptor.Tags)
                {
                    if (IsConcrete(tag))
                    {
                        set.Add(tag.Trim());
                    }
                }
            }

            if (additionalTags is not null)
            {
                foreach (var tag in additionalTags)
                {
                    if (IsConcrete(tag))
                    {
                        set.Add(tag.Trim());
                    }
                }
            }

            return set.Count == 0 ? Array.Empty<string>() : set.ToArray();
        }

        private static bool IsConcrete(string? tag)
            => !string.IsNullOrWhiteSpace(tag) && tag!.IndexOf('{') < 0;
    }
}

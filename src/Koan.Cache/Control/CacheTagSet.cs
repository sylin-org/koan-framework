using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Stores;

namespace Koan.Cache;

public readonly struct CacheTagSet
{
    private readonly ICacheClient _client;
    private readonly string[] _tags;

    internal CacheTagSet(ICacheClient client, IEnumerable<string> tags)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _tags = Normalize(tags);
    }

    public ValueTask<long> Flush(CancellationToken ct = default)
        => _tags.Length == 0
            ? ValueTask.FromResult(0L)
            : _client.FlushTagsAsync(_tags, ct);

    public ValueTask<long> Count(CancellationToken ct = default)
        => _tags.Length == 0
            ? ValueTask.FromResult(0L)
            : _client.CountTagsAsync(_tags, ct);

    public async ValueTask<bool> Any(CancellationToken ct = default)
    {
        if (_tags.Length == 0)
        {
            return false;
        }

        var count = await _client.CountTagsAsync(_tags, ct).ConfigureAwait(false);
        return count > 0;
    }

    private static string[] Normalize(IEnumerable<string> tags)
    {
        if (tags is null)
        {
            return Array.Empty<string>();
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            set.Add(tag.Trim());
        }

        return set.Count == 0 ? Array.Empty<string>() : set.ToArray();
    }
}

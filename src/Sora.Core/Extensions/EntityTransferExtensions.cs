namespace Sora.Core.Extensions;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public static class EntityTransferExtensions
{
    // Simple shape-to-shape transfer via System.Text.Json round-trip for convenience
    public static TTarget TransferTo<TTarget>(this object source)
        where TTarget : class
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<TTarget>(json)!;
    }

    public static IEnumerable<TTarget> TransferTo<TTarget>(this IEnumerable<object> source)
        where TTarget : class
        => source.Select(s => s.TransferTo<TTarget>());
}

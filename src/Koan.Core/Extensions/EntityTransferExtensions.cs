namespace Koan.Core.Extensions;

using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

public static class EntityTransferExtensions
{
    // Simple shape-to-shape transfer via JSON round-trip for convenience
    public static TTarget TransferTo<TTarget>(this object source)
        where TTarget : class
    {
        var json = JsonConvert.SerializeObject(source);
        return JsonConvert.DeserializeObject<TTarget>(json)!;
    }

    public static IEnumerable<TTarget> TransferTo<TTarget>(this IEnumerable<object> source)
        where TTarget : class
        => source.Select(s => s.TransferTo<TTarget>());
}

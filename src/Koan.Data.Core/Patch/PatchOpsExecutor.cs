using System;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Core.Patch;

public static class PatchOpsExecutor
{
    public static void Apply<TEntity, TKey>(TEntity target, PatchPayload<TKey> payload)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        foreach (var op in payload.Ops)
        {
            switch (op.Op.ToLowerInvariant())
            {
                case "add":
                case "replace":
                    SetValueAtPointer<TEntity>(target, op.Path, op.Value);
                    break;
                case "remove":
                    RemoveAtPointer<TEntity>(target, op.Path, payload.Options);
                    break;
                case "copy":
                case "move":
                case "test":
                    throw new NotSupportedException($"Patch op '{op.Op}' not supported in fallback executor.");
                default:
                    throw new InvalidOperationException($"Unknown patch op '{op.Op}'.");
            }
        }
    }

    private static void SetValueAtPointer<TEntity>(TEntity target, string path, JToken? value)
        where TEntity : class
    {
        var obj = JObject.FromObject(target);
        if (string.Equals(path, "/id", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Identity mutation is not allowed via patch.");
        }
        SetAtPointer(obj, path, value ?? JValue.CreateNull());
        Populate(target, obj);
    }

    private static void RemoveAtPointer<TEntity>(TEntity target, string path, PatchOptions? options)
        where TEntity : class
    {
        if (string.Equals(path, "/id", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Identity mutation is not allowed via patch.");
        }
        var obj = JObject.FromObject(target);
        RemoveAtPointer(obj, path);
        Populate(target, obj);
    }

    private static void SetAtPointer(JObject obj, string pointer, JToken value)
    {
        var (parent, leaf) = ResolveParent(obj, pointer);
        parent[leaf] = value;
    }

    private static void RemoveAtPointer(JObject obj, string pointer)
    {
        var (parent, leaf) = ResolveParent(obj, pointer);
        parent[leaf] = JValue.CreateNull();
    }

    private static (JObject parent, string leaf) ResolveParent(JObject root, string pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer) || !pointer.StartsWith('/'))
            throw new ArgumentException("Invalid JSON Pointer.");
        var segments = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) throw new ArgumentException("Invalid JSON Pointer.");

        JObject cur = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var seg = Unescape(segments[i]);
            var propName = FindExistingPropertyName(cur, seg) ?? seg;
            var child = cur[propName] as JObject;
            if (child is null)
            {
                child = new JObject();
                cur[propName] = child;
            }
            cur = child;
        }
        var leafSeg = Unescape(segments[^1]);
        var leaf = FindExistingPropertyName(cur, leafSeg) ?? leafSeg;
        return (cur, leaf);
    }

    private static string? FindExistingPropertyName(JObject obj, string name)
    {
        foreach (var p in obj.Properties())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Name;
        }
        return null;
    }

    private static string Unescape(string segment) => segment.Replace("~1", "/").Replace("~0", "~");

    private static void Populate<TEntity>(TEntity target, JObject obj)
        where TEntity : class
    {
        // Merge current target with obj (which contains desired changes) and propagate nulls
        var destObj = JObject.FromObject(target);
        destObj.Merge(obj, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge });
        using var reader = destObj.CreateReader();
        var serializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
        serializer.Populate(reader, target);
    }
}
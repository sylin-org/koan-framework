using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.JsonPatch;
using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions.Instructions;

namespace Koan.Web.PatchOps;

/// <summary>
/// Static helper for normalizing various patch formats (JSON Patch, Merge Patch, Partial JSON)
/// into a unified PatchPayload format.
/// Pure functions with no dependencies - all parameters passed explicitly.
/// Thread-safe by design with no mutable state.
/// </summary>
public static class PatchNormalizer
{
    /// <summary>
    /// Normalizes a JSON Patch document (RFC 6902) to PatchPayload.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being patched</typeparam>
    /// <typeparam name="TKey">The entity key type</typeparam>
    /// <param name="id">The entity identifier</param>
    /// <param name="doc">The JSON Patch document</param>
    /// <param name="options">Patch options for null handling</param>
    /// <returns>Normalized PatchPayload</returns>
    public static PatchPayload<TKey> NormalizeJsonPatch<TEntity, TKey>(
        TKey id,
        JsonPatchDocument<TEntity> doc,
        PatchOptions options)
        where TEntity : class
        where TKey : notnull
    {
        var ops = doc.Operations.Select(o => new PatchOp(
            o.op,
            o.path,
            o.from,
            o.value is null ? null : JToken.FromObject(o.value))
        ).ToList();

        return new PatchPayload<TKey>(id, null, null, "json-patch", ops, options);
    }

    /// <summary>
    /// Normalizes a Merge Patch document (RFC 7396) to PatchPayload.
    /// Merge semantics: null values mean "remove field".
    /// </summary>
    /// <typeparam name="TKey">The entity key type</typeparam>
    /// <param name="id">The entity identifier</param>
    /// <param name="body">The merge patch JSON object</param>
    /// <param name="options">Patch options for null handling</param>
    /// <returns>Normalized PatchPayload</returns>
    public static PatchPayload<TKey> NormalizeMergePatch<TKey>(
        TKey id,
        JToken body,
        PatchOptions options)
        where TKey : notnull
    {
        return NormalizeObjectToOps(id, body, "merge-patch", mergeSemantics: true, options);
    }

    /// <summary>
    /// Normalizes a partial JSON document to PatchPayload.
    /// Partial semantics: null values mean "set to null".
    /// </summary>
    /// <typeparam name="TKey">The entity key type</typeparam>
    /// <param name="id">The entity identifier</param>
    /// <param name="body">The partial JSON object</param>
    /// <param name="options">Patch options for null handling</param>
    /// <returns>Normalized PatchPayload</returns>
    public static PatchPayload<TKey> NormalizePartialJson<TKey>(
        TKey id,
        JToken body,
        PatchOptions options)
        where TKey : notnull
    {
        return NormalizeObjectToOps(id, body, "partial-json", mergeSemantics: false, options);
    }

    /// <summary>
    /// Internal helper: converts a JSON object to a list of patch operations.
    /// </summary>
    private static PatchPayload<TKey> NormalizeObjectToOps<TKey>(
        TKey id,
        JToken body,
        string kindHint,
        bool mergeSemantics,
        PatchOptions options)
        where TKey : notnull
    {
        var ops = new List<PatchOp>();

        void Walk(JToken token, string basePath)
        {
            if (token is JObject obj)
            {
                foreach (var p in obj.Properties())
                {
                    var path = basePath + "/" + p.Name;
                    if (p.Value.Type == JTokenType.Object)
                    {
                        Walk(p.Value, path);
                    }
                    else if (p.Value.Type == JTokenType.Null)
                    {
                        if (mergeSemantics)
                            ops.Add(new PatchOp("remove", path, null, null));
                        else
                            ops.Add(new PatchOp("replace", path, null, JValue.CreateNull()));
                    }
                    else
                    {
                        ops.Add(new PatchOp("replace", path, null, p.Value.DeepClone()));
                    }
                }
            }
            else
            {
                // Primitives/arrays at root -> replace entire document path
                ops.Add(new PatchOp("replace", basePath, null, token.DeepClone()));
            }
        }

        Walk(body, "");

        // Ensure pointers start at root ('/prop')
        ops = ops.Select(o => o with { Path = o.Path.StartsWith('/') ? o.Path : "/" + o.Path.TrimStart('/') }).ToList();

        return new PatchPayload<TKey>(id, null, null, kindHint, ops, options);
    }
}

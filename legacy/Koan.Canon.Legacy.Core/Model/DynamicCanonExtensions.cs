using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Koan.Canon.Model;

/// <summary>
/// Extension methods for working with DynamicCanonEntity models.
/// Provides beautiful DX for converting dictionaries and objects to Canon entities.
/// </summary>
public static class DynamicCanonExtensions
{
    /// <summary>
    /// Converts a dictionary with JSON path keys to a DynamicCanonEntity.
    /// Example: ["identifier.username"] = "jdoe" creates nested structure.
    /// </summary>
    public static T ToDynamicCanonEntity<T>(this Dictionary<string, object?> pathValues)
        where T : class, IDynamicCanonEntity, new()
    {
        var entity = new T();

        var idProp = typeof(T).GetProperty("Id");
        if (idProp != null && idProp.CanWrite)
        {
            idProp.SetValue(entity, Guid.CreateVersion7().ToString("n"));
        }

        var modelProp = typeof(T).GetProperty("Model");
        if (modelProp != null && modelProp.CanWrite)
        {
            var model = new JObject();
            foreach (var (path, value) in pathValues.Where(kv => kv.Value != null))
            {
                SetValueByPath(model, path, value);
            }
            modelProp.SetValue(entity, model);
        }

        return entity;
    }

    /// <summary>
    /// Converts a nested anonymous object or typed object to a DynamicCanonEntity.
    /// </summary>
    public static T ToDynamicCanonEntity<T>(this object nestedData)
        where T : class, IDynamicCanonEntity, new()
    {
        var entity = new T();

        var idProp = typeof(T).GetProperty("Id");
        if (idProp != null && idProp.CanWrite)
        {
            idProp.SetValue(entity, Guid.CreateVersion7().ToString("n"));
        }

        var modelProp = typeof(T).GetProperty("Model");
        if (modelProp != null && modelProp.CanWrite)
        {
            modelProp.SetValue(entity, JObject.FromObject(nestedData));
        }

        return entity;
    }

    /// <summary>
    /// Fluent method to set a value at a JSON path.
    /// </summary>
    public static T WithPath<T>(this T entity, string path, object? value)
        where T : class, IDynamicCanonEntity
    {
        var modelProp = typeof(T).GetProperty("Model");
        if (modelProp != null)
        {
            var model = modelProp.GetValue(entity) as JObject ?? new JObject();
            if (value != null)
            {
                SetValueByPath(model, path, value);
            }
            modelProp.SetValue(entity, model);
        }
        return entity;
    }

    /// <summary>
    /// Gets a value from the entity by JSON path.
    /// </summary>
    public static TValue? GetPathValue<T, TValue>(this T entity, string jsonPath) where T : class, IDynamicCanonEntity
    {
        var modelProp = typeof(T).GetProperty("Model");
        if (modelProp == null) return default;

        var model = modelProp.GetValue(entity) as JObject;
        if (model == null) return default;

        return GetValueByPath<TValue>(model, jsonPath);
    }

    /// <summary>
    /// Sets a value in the entity by JSON path.
    /// </summary>
    public static void SetPathValue<T>(this T entity, string jsonPath, object? value) where T : class, IDynamicCanonEntity
    {
        var modelProp = typeof(T).GetProperty("Model");
        if (modelProp != null)
        {
            var model = modelProp.GetValue(entity) as JObject ?? new JObject();
            if (value != null)
            {
                SetValueByPath(model, jsonPath, value);
            }
            modelProp.SetValue(entity, model);
        }
    }

    /// <summary>
    /// Extracts aggregation key values from the entity based on configured paths.
    /// </summary>
    public static Dictionary<string, string?> ExtractAggregationValues<T>(this T entity, string[] aggregationKeys)
        where T : class, IDynamicCanonEntity
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in aggregationKeys)
        {
            var value = entity.GetPathValue<T, object>(key);
            result[key] = value?.ToString();
        }

        return result;
    }

    // Private helper methods

    private static void SetValueByPath(JObject jObject, string path, object? value)
    {
        if (string.IsNullOrWhiteSpace(path) || value == null) return;

        var segments = path.Split('.');
        JToken token = jObject;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (token[segment] is not JObject)
            {
                token[segment] = new JObject();
            }
            token = token[segment]!;
        }

        token[segments[^1]] = JToken.FromObject(value);
    }

    private static T? GetValueByPath<T>(JObject jObject, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return default;

        var token = jObject.SelectToken(path);

        if (token != null)
        {
            return token.ToObject<T>();
        }

        return default;
    }
}

/// <summary>
/// Non-generic extension methods for DynamicCanonEntity base class.
/// </summary>
public static class DynamicCanonEntityExtensions
{
    /// <summary>
    /// Gets a value from any DynamicCanonEntity by JSON path.
    /// </summary>
    public static T? GetPathValue<T>(this object entity, string jsonPath)
    {
        var modelProp = entity.GetType().GetProperty("Model");
        if (modelProp == null) return default;

        var model = modelProp.GetValue(entity) as JObject;
        if (model == null) return default;

        var token = model.SelectToken(jsonPath);
        if (token != null)
        {
            return token.ToObject<T>();
        }

        return default;
    }

    private static Dictionary<string, object?> BuildBagFromDynamicEntity<T>(T entity) where T : class, IDynamicCanonEntity
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (entity is null) return dict;

        if (entity.Model is JObject jObject)
        {
            var flattened = FlattenJObject(jObject);
            foreach (var kvp in flattened)
            {
                dict[kvp.Key] = kvp.Value;
            }
        }

        return dict;
    }

    /// <summary>
    /// Flattens a JObject back to JSON path notation (e.g., "identifier.code" = "MFG001")
    /// </summary>
    public static Dictionary<string, object?> FlattenJObject(JObject jObject, string prefix = "")
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in jObject.Properties())
        {
            var currentPath = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";

            if (property.Value is JObject nested)
            {
                var nestedFlattened = FlattenJObject(nested, currentPath);
                foreach (var nestedKvp in nestedFlattened)
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else if (property.Value.Type != JTokenType.Null)
            {
                result[currentPath] = property.Value.ToObject<object>();
            }
        }

        return result;
    }
}



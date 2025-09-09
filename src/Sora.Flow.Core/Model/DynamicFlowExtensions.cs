using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sora.Core.Hosting.App;

namespace Sora.Flow.Model;

/// <summary>
/// Extension methods for working with DynamicFlowEntity models.
/// Provides beautiful DX for converting dictionaries and objects to Flow entities.
/// </summary>
public static class DynamicFlowExtensions
{
    /// <summary>
    /// Converts a dictionary with JSON path keys to a DynamicFlowEntity.
    /// Example: ["identifier.username"] = "jdoe" creates nested structure.
    /// </summary>
    public static T ToDynamicFlowEntity<T>(this Dictionary<string, object?> pathValues)
        where T : class, IDynamicFlowEntity, new()
    {
        var entity = new T();
        
        // Set Id if the entity has this property
        var idProp = typeof(T).GetProperty("Id");
        if (idProp != null && idProp.CanWrite)
        {
            idProp.SetValue(entity, Guid.NewGuid().ToString("n"));
        }
        
        // Set Model if the entity has this property
        var modelProp = typeof(T).GetProperty("Model");
        if (modelProp != null && modelProp.CanWrite)
        {
            modelProp.SetValue(entity, new ExpandoObject());
        }
        
        // Add values to Model if it exists
        if (modelProp != null)
        {
            var model = modelProp.GetValue(entity) as ExpandoObject;
            if (model != null)
            {
                foreach (var (path, value) in pathValues.Where(kv => kv.Value != null))
                {
                    SetValueByPath(model, path, value);
                }
                
                // Clean up the Model to ensure MongoDB-compatible types
                var finalModel = modelProp.GetValue(entity) as ExpandoObject;
                if (finalModel != null)
                {
                    DynamicFlowEntityExtensions.CleanExpandoObjectForMongoDB(finalModel);
                }
                
                // Verify Model is still accessible after population and cleanup
                var modelKeys = finalModel != null ? string.Join(", ", ((IDictionary<string, object?>)finalModel).Keys) : "null";
            }
        }
        
        return entity;
    }
    
    /// <summary>
    /// Converts a nested anonymous object or typed object to a DynamicFlowEntity.
    /// </summary>
    public static T ToDynamicFlowEntity<T>(this object nestedData)
        where T : class, IDynamicFlowEntity, new()
    {
        var entity = new T();
        
        // Set Id if the entity has this property
        var idProp = typeof(T).GetProperty("Id");
        if (idProp != null && idProp.CanWrite)
        {
            idProp.SetValue(entity, Guid.NewGuid().ToString("n"));
        }
        
        // Set Model if the entity has this property
        var modelProp = typeof(T).GetProperty("Model");
        if (modelProp != null && modelProp.CanWrite)
        {
            modelProp.SetValue(entity, nestedData.ToExpando());
        }
        
        return entity;
    }
    
    /// <summary>
    /// Fluent method to set a value at a JSON path.
    /// </summary>
    public static T WithPath<T>(this T entity, string path, object? value)
        where T : class, IDynamicFlowEntity
    {
        var modelProp = typeof(T).GetProperty("Model");
        if (modelProp != null)
        {
            var model = modelProp.GetValue(entity) as ExpandoObject ?? new ExpandoObject();
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
    public static TValue? GetPathValue<T, TValue>(this T entity, string jsonPath) where T : class, IDynamicFlowEntity
    {
        var modelProp = typeof(T).GetProperty("Model");
        if (modelProp == null) return default;
        
        var model = modelProp.GetValue(entity) as ExpandoObject;
        if (model == null) return default;
        
        return GetValueByPath<TValue>(model, jsonPath);
    }
    
    /// <summary>
    /// Sets a value in the entity by JSON path.
    /// </summary>
    public static void SetPathValue<T>(this T entity, string jsonPath, object? value) where T : class, IDynamicFlowEntity
    {
        var modelProp = typeof(T).GetProperty("Model");
        if (modelProp != null)
        {
            var model = modelProp.GetValue(entity) as ExpandoObject ?? new ExpandoObject();
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
        where T : class, IDynamicFlowEntity
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
    
    private static void SetValueByPath(ExpandoObject expando, string path, object? value)
    {
        if (string.IsNullOrWhiteSpace(path) || value == null) return;
        
        var dict = (IDictionary<string, object?>)expando;
        var segments = path.Split('.');
        
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            
            if (!dict.ContainsKey(segment) || dict[segment] is not ExpandoObject)
            {
                dict[segment] = new ExpandoObject();
            }
            
            dict = (IDictionary<string, object?>)dict[segment]!;
        }
        
        dict[segments[^1]] = value;
    }
    
    private static T? GetValueByPath<T>(ExpandoObject expando, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return default;
        
        var dict = (IDictionary<string, object?>)expando;
        var segments = path.Split('.');
        
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            
            if (!dict.ContainsKey(segment) || dict[segment] is not ExpandoObject)
            {
                return default;
            }
            
            dict = (IDictionary<string, object?>)dict[segment]!;
        }
        
        if (dict.TryGetValue(segments[^1], out var value) && value is not null)
        {
            if (value is T typedValue)
                return typedValue;

            // Try conversion for common types when value is convertible
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }
        
        return default;
    }
    
    private static ExpandoObject ToExpando(this object obj)
    {
        if (obj is ExpandoObject expando)
            return expando;
        
        // Use JSON serialization for deep conversion
        var json = JsonConvert.SerializeObject(obj);
        var jObject = JObject.Parse(json);
        return jObject.ToExpando();
    }
    
    private static ExpandoObject ToExpando(this JObject jObject)
    {
        var expando = new ExpandoObject();
        var dict = (IDictionary<string, object?>)expando;
        
        foreach (var property in jObject.Properties())
        {
            dict[property.Name] = property.Value.Type switch
            {
                JTokenType.Object => ((JObject)property.Value).ToExpando(),
                JTokenType.Array => property.Value.ToObject<object>(),
                _ => ((JValue)property.Value).Value
            };
        }
        
        return expando;
    }
}

/// <summary>
/// Non-generic extension methods for DynamicFlowEntity base class.
/// </summary>
public static class DynamicFlowEntityExtensions
{
    /// <summary>
    /// Gets a value from any DynamicFlowEntity by JSON path.
    /// </summary>
    public static T? GetPathValue<T>(this object entity, string jsonPath) 
    {
        var modelProp = entity.GetType().GetProperty("Model");
        if (modelProp == null) return default;
        
        var model = modelProp.GetValue(entity) as ExpandoObject;
        if (model == null) return default;
        
        var dict = (IDictionary<string, object?>)model;
        var segments = jsonPath.Split('.');
        
        for (int i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            
            if (!dict.ContainsKey(segment) || dict[segment] is not ExpandoObject)
            {
                return default;
            }
            
            dict = (IDictionary<string, object?>)dict[segment]!;
        }
        
        if (dict.TryGetValue(segments[^1], out var value) && value is not null)
        {
            if (value is T typedValue)
                return typedValue;

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }
        
        return default;
    }


    private static Dictionary<string, object?> BuildBagFromDynamicEntity<T>(T entity) where T : class, IDynamicFlowEntity
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (entity is null) return dict;


        // Extract all flattened paths from the ExpandoObject Model
        if (entity.Model is ExpandoObject expando)
        {
            var flattened = FlattenExpandoObject(expando);
            foreach (var kvp in flattened)
            {
                dict[kvp.Key] = kvp.Value;
            }
        }

        return dict;
    }

    /// <summary>
    /// Flattens an ExpandoObject back to JSON path notation (e.g., "identifier.code" = "MFG001")
    /// </summary>
    public static Dictionary<string, object?> FlattenExpandoObject(ExpandoObject expando, string prefix = "")
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var dict = (IDictionary<string, object?>)expando;
        
        foreach (var kvp in dict)
        {
            var currentPath = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";

            if (kvp.Value is ExpandoObject nested)
            {
                // Recursively flatten nested ExpandoObjects
                var nestedFlattened = FlattenExpandoObject(nested, currentPath);
                foreach (var nestedKvp in nestedFlattened)
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else if (kvp.Value != null)
            {
                // Convert JsonElement values to proper .NET types for MongoDB serialization
                var convertedValue = ConvertJsonElementToClrType(kvp.Value);
                if (convertedValue != null)
                {
                    // If conversion resulted in another ExpandoObject, recursively flatten it
                    if (convertedValue is ExpandoObject convertedExpando)
                    {
                        var nestedFlattened = FlattenExpandoObject(convertedExpando, currentPath);
                        foreach (var nestedKvp in nestedFlattened)
                        {
                            result[nestedKvp.Key] = nestedKvp.Value;
                        }
                    }
                    else
                    {
                        result[currentPath] = convertedValue;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Converts JsonElement objects and Newtonsoft.Json types to proper .NET types for MongoDB serialization compatibility
    /// </summary>
    private static object? ConvertJsonElementToClrType(object? value)
    {
        if (value is null) return null;

        // Handle Newtonsoft.Json types that can't be serialized by MongoDB
        if (value is JArray jArray)
        {
            return jArray.Select(token => ConvertJsonElementToClrType(token.ToObject<object>())).ToArray();
        }
        
        if (value is JObject jObject)
        {
            // Inline ToExpando logic to avoid accessibility issues
            var expando = new ExpandoObject();
            var dict = (IDictionary<string, object?>)expando;
            foreach (var property in jObject.Properties())
            {
                dict[property.Name] = property.Value.Type switch
                {
                    JTokenType.Object => ConvertJsonElementToClrType((JObject)property.Value),
                    JTokenType.Array => ConvertJsonElementToClrType(property.Value.ToObject<object>()),
                    _ => ((JValue)property.Value).Value
                };
            }
            return expando;
        }
        
        if (value is JValue jValue)
        {
            return jValue.Value;
        }
        
        if (value is JToken jToken)
        {
            return ConvertJsonElementToClrType(jToken.ToObject<object>());
        }

        // Handle JsonElement conversion
        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString(),
                JsonValueKind.Number => jsonElement.TryGetInt64(out var longVal) ? longVal : jsonElement.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Array => jsonElement.EnumerateArray().Select(el => ConvertJsonElementToClrType(el)).ToArray(),
                JsonValueKind.Object => ConvertJsonObjectToExpando(jsonElement),
                _ => jsonElement.ToString()
            };
        }

        return value;
    }

    /// <summary>
    /// Converts a JsonElement object to an ExpandoObject
    /// </summary>
    private static ExpandoObject ConvertJsonObjectToExpando(JsonElement jsonElement)
    {
        var expando = new ExpandoObject();
        var dict = (IDictionary<string, object?>)expando;

        foreach (var property in jsonElement.EnumerateObject())
        {
            dict[property.Name] = ConvertJsonElementToClrType(property.Value);
        }

        return expando;
    }
    
    /// <summary>
    /// Recursively cleans an ExpandoObject in-place to ensure all values are MongoDB BSON compatible
    /// Converts JArray, JObject, and other Newtonsoft.Json types to proper .NET types
    /// </summary>
    public static void CleanExpandoObjectForMongoDB(ExpandoObject expando)
    {
        var dict = (IDictionary<string, object?>)expando;
        
        foreach (var kvp in dict.ToList()) // ToList() to avoid modifying during iteration
        {
            var cleanedValue = ConvertJsonElementToClrType(kvp.Value);
            
            // Filter out null values to prevent BSON serialization issues
            if (cleanedValue == null)
            {
                dict.Remove(kvp.Key);
                continue;
            }
            
            if (cleanedValue != kvp.Value) // Only update if the value changed
            {
                dict[kvp.Key] = cleanedValue;
            }
            
            // Recursively clean nested ExpandoObjects
            if (cleanedValue is ExpandoObject nestedExpando)
            {
                CleanExpandoObjectForMongoDB(nestedExpando);
            }
        }
    }
}
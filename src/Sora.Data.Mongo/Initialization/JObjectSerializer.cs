using System;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sora.Core.Hosting.App;

namespace Sora.Data.Mongo.Initialization;

/// <summary>
/// Serializes a <see cref="JObject"/> to and from BSON.
/// </summary>
public class JObjectSerializer : SerializerBase<JObject>, IBsonSerializer<object>
{
    private readonly ILogger? _logger;

    public JObjectSerializer()
    {
        // It's possible that the ServiceProvider is not available when this serializer is registered
        // so we need to handle the case where the logger is not available.
        _logger = (ILogger?)AppHost.Current?.GetService(typeof(ILogger<JObjectSerializer>));
    }
    
    object IBsonSerializer<object>.Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        return Deserialize(context, args);
    }

    void IBsonSerializer<object>.Serialize(BsonSerializationContext context, BsonSerializationArgs args, object value)
    {
        if (value is JObject jObject)
        {
            Serialize(context, args, jObject);
        }
        else
        {
            // Fallback for other object types if necessary
            var objectSerializer = new ObjectSerializer();
            objectSerializer.Serialize(context, args, value);
        }
    }
    
    public override JObject Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonReader = context.Reader;
        
        // Use the driver's built-in serializer to read any BsonValue
        var bsonValue = BsonValueSerializer.Instance.Deserialize(context);

        if (bsonValue.IsBsonNull)
        {
            return null!;
        }

        // If it's already a document, we can convert it directly.
        if (bsonValue.IsBsonDocument)
        {
            // The ToJson() method produces a well-formed JSON string.
            var json = bsonValue.AsBsonDocument.ToJson();
            return JObject.Parse(json);
        }
        
        // If it's any other type (string, int, bool, etc.), it's a primitive.
        // The goal is to represent this primitive as a JObject.
        // We'll wrap it in a standard structure: { "value": <primitive> }
        
        // BsonTypeMapper can convert the BsonValue to its corresponding .NET type.
        var dotNetValue = BsonTypeMapper.MapToDotNetValue(bsonValue);

        // JToken.FromObject can handle all primitive .NET types correctly.
        var jToken = JToken.FromObject(dotNetValue);
        
        return new JObject { ["value"] = jToken };
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, JObject value)
    {
        if (value == null)
        {
            context.Writer.WriteNull();
            return;
        }

        var json = value.ToString(Formatting.None);
        // _logger?.LogDebug("[JObjectSerializer] Serializing JObject: {Json}", json);
        
        try
        {
            var document = BsonSerializer.Deserialize<BsonDocument>(json);
            BsonDocumentSerializer.Instance.Serialize(context, document);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[JObjectSerializer] Failed to serialize JObject as BsonDocument. JObject value: {Json}. Falling back to writing as string.", json);
            // Fallback for simple JObject like { "value": "some_string" }
            context.Writer.WriteString(value.ToString());
        }
    }
}

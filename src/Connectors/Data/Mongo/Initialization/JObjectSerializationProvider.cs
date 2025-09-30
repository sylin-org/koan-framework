using MongoDB.Bson.Serialization;
using Newtonsoft.Json.Linq;
using System;

namespace Koan.Data.Connector.Mongo.Initialization
{
    public class JObjectSerializationProvider : IBsonSerializationProvider
    {
        public IBsonSerializer? GetSerializer(Type type)
        {
            if (type == typeof(JObject))
            {
                return new JObjectSerializer();
            }

            // For properties of type object that might contain a JObject
            if (type == typeof(object))
            {
                return new JObjectSerializer();
            }

            return null;
        }
    }
}


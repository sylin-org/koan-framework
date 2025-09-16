using System;
using Koan.Data.Core.Relationships;

namespace Koan.Data.Core.Model
{
    public static class EntityMetadataProvider
    {
        public static Func<IServiceProvider, IRelationshipMetadata>? RelationshipMetadataAccessor;
    }
}

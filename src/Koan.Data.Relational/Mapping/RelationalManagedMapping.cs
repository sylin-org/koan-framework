using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Polymorphism;
using Newtonsoft.Json;

namespace Koan.Data.Relational.Mapping;

/// <summary>Compiles the relational family's managed Id + structured-object convention.</summary>
public static class RelationalManagedMapping
{
    public static MappingPlan Compile<TEntity>(
        string source,
        StorageAddress container,
        string keyName = "Id",
        string objectName = "Json")
        where TEntity : class
    {
        var convention = MappingPlanCompiler.Convention(
            source,
            typeof(TEntity),
            new MappingConvention(container, keyName, objectName));
        var codec = new EntityObjectCodec<TEntity>(convention.Identity.LogicalPath);
        var bindings = convention.Bindings.Select(binding =>
            binding.LogicalPath.IsRoot && binding.Shape == MappingValueShape.Object
                ? new MappingBindingDescriptor(
                    binding.Id,
                    binding.LogicalPath,
                    binding.Role,
                    binding.LogicalType,
                    binding.PhysicalPath,
                    binding.Shape,
                    binding.Direction,
                    binding.Generation,
                    binding.Authority,
                    codec)
                : binding).ToArray();
        return MappingPlanCompiler.Compile(new MappingDescriptor(
            convention.Source,
            convention.EntityType,
            convention.Container,
            convention.Identity,
            bindings));
    }

    private sealed class EntityObjectCodec<TEntity> : IRootObjectMappingCodec
        where TEntity : class
    {
        private readonly MappingPath _identity;
        private readonly RelationalStructuredValueCodec _values = new();
        private readonly JsonSerializerSettings _settings = ComparableScalarEncoding.Apply(new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            // Hydration is store-authoritative (PMC-061). Newtonsoft's default ObjectCreationHandling.Auto
            // populates an already-populated collection: a materialized instance's constructor-seeded entries
            // would survive hydration AND gain the stored history on top, so every save/reload cycle duplicated
            // constructor artifacts (observed as growing "Stage created" transitions on CanonStage receipts).
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        });

        public EntityObjectCodec(MappingPath identity)
        {
            _identity = identity;
            ExcludedLogicalPaths = new HashSet<MappingPath> { identity };
        }

        public string Id => "koan.relational.entity-object.v1";
        public Type LogicalType => typeof(TEntity);
        public Type PhysicalType => typeof(DataObject);
        public bool CanEncode => true;
        public bool CanDecode => true;
        public IReadOnlySet<MappingPath> ExcludedLogicalPaths { get; }

        public object? Encode(object? logical)
        {
            if (logical is null) return null;
            if (logical is not TEntity entity)
                throw new InvalidCastException($"Managed relational storage expected '{typeof(TEntity).FullName}'.");
            var document = _values.Deserialize(JsonConvert.SerializeObject(entity, entity.GetType(), _settings)) as DataObject
                ?? throw new InvalidDataException("Managed Entity JSON must be one object.");
            var identityName = _identity.Segments[0];
            return new DataObject(document.Properties.Where(property =>
                !string.Equals(property.Name, identityName, StringComparison.OrdinalIgnoreCase)));
        }

        public object? Decode(object? physical)
        {
            if (physical is null) return null;
            if (physical is not DataObject document)
                throw new InvalidCastException("Managed relational storage expected one structured object.");
            return JsonConvert.DeserializeObject(_values.Serialize(document), typeof(TEntity), _settings)
                ?? throw new InvalidDataException($"Managed Entity JSON could not materialize '{typeof(TEntity).FullName}'.");
        }
    }
}

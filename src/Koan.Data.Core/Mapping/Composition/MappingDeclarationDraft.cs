using Koan.Data.Abstractions;

namespace Koan.Data.Core.Mapping.Composition;

internal sealed class MappingDeclarationDraft
{
    private readonly List<MappingBindingDraft> _bindings = [];
    private MappingPath? _keyPath;
    private Type? _keyType;
    private List<MappingBindingDraft>? _keyParts;

    public MappingDeclarationDraft(string source, Type entityType)
    {
        Source = source;
        EntityType = entityType;
    }

    public string Source { get; }
    public Type EntityType { get; }
    public StorageAddress? Container { get; set; }

    public MappingBindingDraft BeginKey(MappingPath path, Type type)
    {
        if (_keyPath is not null)
            throw Error("Declare Key exactly once.");
        _keyPath = path;
        _keyType = type;
        return new MappingBindingDraft(path, MappingRole.Key, type);
    }

    public MappingBindingDraft BeginProperty(MappingPath path, Type type) =>
        new(path, MappingRole.Property, type);

    public MappingBindingDraft BeginRootObject() =>
        new(MappingPath.Root, MappingRole.Object, EntityType);

    public void Add(MappingBindingDraft binding)
    {
        if (!binding.IsLocated)
            throw Error($"Logical value '{binding.LogicalPath}' has no Name, Path, or Object location.");
        _bindings.Add(binding);
        if (binding.Role == MappingRole.Key) _keyParts = [binding];
    }

    public void AddComposite(IReadOnlyList<MappingBindingDraft> parts)
    {
        if (parts.Count < 2)
            throw Error("Composite identity requires at least two complete Parts.Property bindings.");
        if (parts.Any(static part => !part.IsLocated))
            throw Error("Every composite identity part requires Name or Path.");
        _keyParts = parts.ToList();
        _bindings.AddRange(parts);
    }

    public MappingDescriptor Build()
    {
        if (Container is null) throw Error("Declare Container before the map is used.");
        if (_keyPath is null || _keyType is null || _keyParts is null)
            throw Error("Declare one complete Key binding.");
        if (_bindings.Count == 0) throw Error("Declare at least one mapping binding.");

        var descriptors = _bindings.Select(ToDescriptor).ToArray();
        var byDraft = _bindings.Select((draft, index) => (draft, descriptor: descriptors[index]))
            .ToDictionary(static pair => pair.draft, static pair => pair.descriptor);
        var identity = new MappingIdentityDescriptor(_keyPath, _keyType, _keyParts.Select(part => byDraft[part]));
        return new MappingDescriptor(Source, EntityType, Container, identity, descriptors);
    }

    public MappingCompilationException Error(string correction) => new(Source, EntityType, correction);

    private static MappingBindingDescriptor ToDescriptor(MappingBindingDraft binding)
    {
        var physical = binding.PhysicalPath!;
        var id = $"{binding.Role}:{binding.LogicalPath}->{physical}";
        return new MappingBindingDescriptor(
            id,
            binding.LogicalPath,
            binding.Role,
            binding.LogicalType,
            physical,
            binding.Shape,
            binding.Direction,
            binding.Generation,
            MappingAuthority.Canonical,
            binding.Codec);
    }
}

internal sealed class MappingBindingDraft
{
    public MappingBindingDraft(MappingPath logicalPath, MappingRole role, Type logicalType)
    {
        LogicalPath = logicalPath;
        Role = role;
        LogicalType = logicalType;
    }

    public MappingPath LogicalPath { get; }
    public MappingRole Role { get; }
    public Type LogicalType { get; }
    public PhysicalPath? PhysicalPath { get; private set; }
    public MappingValueShape Shape { get; private set; } = MappingValueShape.Scalar;
    public MappingDirection Direction { get; set; } = MappingDirection.ReadWrite;
    public MappingGeneration Generation { get; set; } = MappingGeneration.Application;
    public IDataMappingCodec? Codec { get; set; }
    public bool IsLocated => PhysicalPath is not null;

    public void Locate(PhysicalPath path, MappingValueShape shape)
    {
        if (PhysicalPath is not null)
            throw new InvalidOperationException($"Logical value '{LogicalPath}' already has physical location '{PhysicalPath}'.");
        PhysicalPath = path;
        Shape = shape;
    }
}

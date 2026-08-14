using System.Text;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Core.KeyValue;
using Koan.Data.Core.Polymorphism;
using Koan.Data.Core.Semantics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Koan.Data.Connector.Json.Runtime;

/// <summary>Translates the KeyValue family primitives to independently replaceable JSON object files.</summary>
internal sealed class JsonIndividualFilesRepository<TEntity, TKey> : KeyValueStore<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private static readonly Type RootType = EntityRootDescriptor.For(typeof(TEntity)).RootType;
    private static readonly JsonSerializerSettings Settings = EntityJsonSerialization.Apply(new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Include
    });
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private readonly JsonRoute _route;
    private readonly JsonIndividualFileRegistry _files;
    private readonly IReadOnlyList<DataSegmentationField> _segmentation;
    private readonly INamingProvider _naming;
    private readonly IServiceProvider _services;

    internal JsonIndividualFilesRepository(
        JsonRoute route,
        JsonIndividualFileRegistry files,
        DataSegmentationPlan segmentation,
        INamingProvider naming,
        IServiceProvider services)
    {
        _route = route;
        _files = files;
        _segmentation = segmentation.For(RootType).Fields;
        _naming = naming;
        _services = services;

        var baseStorage = _naming.ResolveStorage(RootType, partition: null, _services);
        var locator = new JsonIndividualFileLocator(_route.DirectoryPath, _route.IndividualFilePath, baseStorage);
        if (!locator.UsesStorageToken)
        {
            _files.ClaimUnqualifiedLayout(
                _route.DirectoryPath,
                _route.IndividualFilePath,
                RootType,
                typeof(TKey));
        }
    }

    protected override async Task<KvRecord<TEntity>?> ReadAsync(TKey id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DemandReadableRoot();
        var locator = CurrentLocator();
        var path = locator.PathFor(id);
        var gate = _files.Gate(path);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadRecord(locator, path, id, validateExpectedId: true, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    protected override Task<IReadOnlyList<KvRecord<TEntity>>> ScanAsync(CancellationToken ct) =>
        ScanCore(int.MaxValue, ct);

    protected override Task<IReadOnlyList<KvRecord<TEntity>>> ScanBoundedAsync(
        int maxCandidates,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCandidates);
        return ScanCore(maxCandidates, ct);
    }

    protected override async Task WriteAsync(TKey id, KvRecord<TEntity> record, CancellationToken ct)
    {
        _route.Policy.Demand(DataOperationEffect.Write, "persist an individual JSON entity file");
        ct.ThrowIfCancellationRequested();
        var payload = Encode(record);
        DemandPayloadBound(payload, "write");

        var locator = CurrentLocator();
        var path = locator.PathFor(id);
        var gate = _files.Gate(path);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_route.Policy.StorageLifecycle == StorageLifecycle.External && !File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"External JSON entity file '{path}' does not exist; Koan will not create it.",
                    path);
            }

            await Persist(path, payload, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    protected override async Task<bool> RemoveAsync(TKey id, CancellationToken ct)
    {
        _route.Policy.Demand(DataOperationEffect.Write, "remove an individual JSON entity file");
        ct.ThrowIfCancellationRequested();
        DemandReadableRoot();
        var locator = CurrentLocator();
        var path = locator.PathFor(id);
        var gate = _files.Gate(path);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_route.Policy.StorageLifecycle == StorageLifecycle.External && !File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"External JSON entity file '{path}' does not exist; Koan will not create it.",
                    path);
            }
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    protected override async Task<int> ClearAsync(CancellationToken ct)
    {
        _route.Policy.Demand(DataOperationEffect.Write, "clear individual JSON entity files");
        ct.ThrowIfCancellationRequested();
        DemandReadableRoot();
        var locator = CurrentLocator();
        var paths = locator.EnumeratePaths().ToArray();

        // Validate every owned document before the first deletion. Corrupt storage is never converted into an empty
        // successful store, even for an explicit clear.
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var gate = _files.Gate(path);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _ = await ReadRecord(locator, path, expectedId: default!, validateExpectedId: false, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        var removed = 0;
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var gate = _files.Gate(path);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!File.Exists(path)) continue;
                File.Delete(path);
                removed++;
            }
            finally
            {
                gate.Release();
            }
        }
        return removed;
    }

    protected override void DescribeBackend(ICapabilities capabilities) =>
        JsonFeatures.DescribeIndividualFilesBackend(capabilities);

    public override async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ct.ThrowIfCancellationRequested();
        if (instruction.Name != DataInstructions.EnsureCreated)
            return await base.ExecuteAsync<TResult>(instruction, ct).ConfigureAwait(false);

        if (_route.Policy.StorageLifecycle != StorageLifecycle.Managed ||
            _route.Policy.Access != DataSourceAccess.ReadWrite)
        {
            DemandReadableRoot();
            return (TResult)(object)true;
        }

        _route.Policy.Demand(DataOperationEffect.SchemaOrAdmin, "ensure individual JSON entity storage");
        Directory.CreateDirectory(_route.DirectoryPath);
        return (TResult)(object)true;
    }

    private async Task<IReadOnlyList<KvRecord<TEntity>>> ScanCore(int maximum, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DemandReadableRoot();
        var locator = CurrentLocator();
        var result = new List<KvRecord<TEntity>>();
        var identities = new HashSet<TKey>();

        foreach (var path in locator.EnumeratePaths())
        {
            ct.ThrowIfCancellationRequested();
            if (result.Count == maximum) break;

            var gate = _files.Gate(path);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var record = await ReadRecord(
                    locator,
                    path,
                    expectedId: default!,
                    validateExpectedId: false,
                    ct).ConfigureAwait(false);
                if (record is null) continue;
                if (!identities.Add(record.Value.Entity.Id))
                {
                    throw new InvalidDataException(
                        $"JSON individual-file source '{_route.Source}' contains duplicate identity " +
                        $"'{record.Value.Entity.Id}'. Repair the duplicate deliberately; ambiguous storage is never accepted.");
                }
                result.Add(record.Value);
            }
            finally
            {
                gate.Release();
            }
        }
        return result;
    }

    private async Task<KvRecord<TEntity>?> ReadRecord(
        JsonIndividualFileLocator locator,
        string path,
        TKey expectedId,
        bool validateExpectedId,
        CancellationToken ct)
    {
        if (!File.Exists(path)) return null;

        var length = new FileInfo(path).Length;
        if (length > Infrastructure.Constants.Provider.MaximumFileBytes)
        {
            throw new InvalidDataException(
                $"Koan JSON entity file '{path}' is {length} bytes and exceeds the 64 MiB safety bound. " +
                "Reduce the record or use a database adapter before reading it.");
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, Utf8, ct).ConfigureAwait(false);
            var document = JObject.Parse(json);
            var managed = ManagedFieldJsonInjector.ExtractManaged(document, typeof(TEntity), _segmentation);
            var entity = EntityJsonSerialization.MaterializeStored(
                document,
                typeof(TEntity),
                JsonSerializer.Create(Settings)) as TEntity
                ?? throw new InvalidDataException(
                    $"JSON record '{path}' could not materialize Entity root '{RootType.FullName}'.");

            if (validateExpectedId && !EqualityComparer<TKey>.Default.Equals(entity.Id, expectedId))
            {
                throw new InvalidDataException(
                    $"JSON entity file '{path}' contains identity '{entity.Id}', not requested identity '{expectedId}'. " +
                    "Repair the file name or stored identity deliberately.");
            }

            var canonical = locator.PathFor(entity.Id);
            if (!JsonFileRegistry.PathComparer.Equals(canonical, Path.GetFullPath(path)))
            {
                throw new InvalidDataException(
                    $"JSON entity file '{path}' contains identity '{entity.Id}', which maps to '{canonical}'. " +
                    "Repair the file placement or stored identity deliberately; ambiguous storage is never accepted.");
            }

            return new KvRecord<TEntity>(entity, managed);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Corrupt(path, exception);
        }
    }

    private static string Encode(KvRecord<TEntity> record)
    {
        var document = JObject.FromObject(record.Entity, JsonSerializer.Create(Settings));
        ManagedFieldJsonInjector.InjectManaged(document, record.Managed);
        return document.ToString(Formatting.None);
    }

    private async Task Persist(string path, string payload, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"JSON entity file '{path}' has no containing directory.");
        if (_route.Policy.StorageLifecycle == StorageLifecycle.Managed)
            Directory.CreateDirectory(parent);

        var temporary = $"{path}.{Guid.CreateVersion7():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, payload, Utf8, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void DemandPayloadBound(string payload, string operation)
    {
        var bytes = Utf8.GetByteCount(payload);
        if (bytes <= Infrastructure.Constants.Provider.MaximumFileBytes) return;
        throw new InvalidOperationException(
            $"JSON {operation} for Entity root '{RootType.FullName}' would be {bytes} bytes and exceeds the " +
            "64 MiB per-record safety bound. Reduce the record or use a database adapter.");
    }

    private void DemandReadableRoot()
    {
        if (Directory.Exists(_route.DirectoryPath)) return;
        if (_route.Policy.StorageLifecycle == StorageLifecycle.External)
        {
            throw new DirectoryNotFoundException(
                $"External JSON source '{_route.Source}' requires existing directory '{_route.DirectoryPath}'; Koan will not create it.");
        }
        if (_route.Policy.Access == DataSourceAccess.ReadOnly)
        {
            throw new DirectoryNotFoundException(
                $"Read-only JSON source '{_route.Source}' requires existing directory '{_route.DirectoryPath}'; Koan will not create it.");
        }
    }

    private JsonIndividualFileLocator CurrentLocator()
    {
        var partition = EntityContext.Current?.Partition;
        var storage = _naming.ResolveStorage(RootType, partition, _services);
        var locator = new JsonIndividualFileLocator(_route.DirectoryPath, _route.IndividualFilePath, storage);
        if (!locator.UsesStorageToken && !string.IsNullOrWhiteSpace(partition))
        {
            throw new InvalidOperationException(
                $"JSON IndividualFilePath '{_route.IndividualFilePath}' omits '{{storage}}' and cannot isolate partition " +
                $"'{partition}'. Include '{{storage}}' or use an unpartitioned dedicated source.");
        }
        return locator;
    }

    private static InvalidDataException Corrupt(string path, Exception exception) => new(
        $"Koan JSON could not read '{path}' because it does not contain a valid Entity object. " +
        "Restore the file from a known-good copy or remove it deliberately; corrupt storage is never treated as empty.",
        exception);
}

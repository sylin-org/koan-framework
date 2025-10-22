using System.Text.Json;
using Koan.Jobs.Model;

namespace Koan.Jobs.Store;

internal readonly record struct JobStoreMetadata(
    JobStorageMode StorageMode,
    string? Source,
    string? Partition,
    bool Audit,
    JsonSerializerOptions SerializerOptions);

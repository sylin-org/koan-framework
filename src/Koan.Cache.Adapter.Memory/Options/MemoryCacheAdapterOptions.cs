using System;
using System.ComponentModel.DataAnnotations;

namespace Koan.Cache.Adapter.Memory.Options;

public sealed class MemoryCacheAdapterOptions
{
    [Range(1, int.MaxValue)]
    public int TagIndexCapacity { get; set; } = 2048;

    public bool EnableStaleWhileRevalidate { get; set; } = true;
}

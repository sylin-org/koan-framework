using System;

namespace Koan.Cache.Abstractions.Adapters;

public sealed record CacheAdapterDescriptor(string Name, Type RegistrarType, string? Description = null);

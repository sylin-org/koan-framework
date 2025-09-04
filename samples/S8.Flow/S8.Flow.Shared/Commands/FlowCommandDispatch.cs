using System;
using System.Collections.Generic;

namespace S8.Flow.Shared.Commands;

public sealed record FlowCommandDispatch(
    string Name,
    string? Target,
    IReadOnlyDictionary<string, object?> Args,
    DateTimeOffset IssuedAt,
    string? Source
);

using System;

namespace Koan.Data.Core.Events;

/// <summary>
/// Supported lifecycle operations for entity events.
/// </summary>
public enum EntityEventOperation
{
    Load,
    Upsert,
    Remove
}

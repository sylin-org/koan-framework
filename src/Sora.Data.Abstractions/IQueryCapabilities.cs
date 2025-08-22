namespace Sora.Data.Abstractions;

public interface IQueryCapabilities
{
    QueryCapabilities Capabilities { get; }
}

// Declared provider capabilities for write paths

// Optional marker interfaces that providers can implement to indicate native bulk semantics
namespace Koan.Core.Diagnostics;

/// <summary>Read-only access to the current host's runtime explanation facts.</summary>
public interface IKoanRuntimeFacts
{
    KoanFactEnvelope Current { get; }
}

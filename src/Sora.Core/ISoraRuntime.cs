using System;

namespace Sora.Core;

/// <summary>
/// Legacy runtime interface. Replaced by Sora.Core.Hosting.Runtime.IAppRuntime.
/// </summary>
[Obsolete("Replaced by Sora.Core.Hosting.Runtime.IAppRuntime; will be removed.")]
public interface ISoraRuntime
{
    void Discover();
    void Start();
}
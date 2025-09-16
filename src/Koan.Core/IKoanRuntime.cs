using System;

namespace Koan.Core;

/// <summary>
/// Legacy runtime interface. Replaced by Koan.Core.Hosting.Runtime.IAppRuntime.
/// </summary>
[Obsolete("Replaced by Koan.Core.Hosting.Runtime.IAppRuntime; will be removed.")]
public interface IKoanRuntime
{
    void Discover();
    void Start();
}
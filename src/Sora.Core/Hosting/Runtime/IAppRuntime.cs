namespace Sora.Core.Hosting.Runtime;

// Greenfield runtime lifecycle. This is the single entry point for discovery and start.
public interface IAppRuntime
{
    void Discover();
    void Start();
}

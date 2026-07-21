namespace Koan.Core.Diagnostics;

/// <summary>
/// Framework-module seam for recording the latest safe fact about a stable runtime subject.
/// This is a snapshot, not an event stream: a later fact with the same code and subject replaces it.
/// </summary>
public interface IKoanRuntimeFactRecorder
{
    void Record(KoanFactDescriptor descriptor);
}

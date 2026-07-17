namespace Koan.Packaging.Models;

internal sealed class PreparedTemplatePackage(string root) : IDisposable
{
    public string Root { get; } = root;

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}

namespace Koan.Data.Abstractions.Failures;

/// <summary>Write-only adapter seam for restricted native evidence; public channels receive only its opaque reference.</summary>
public interface IDataNativeEvidenceSink
{
    string Record(
        Exception nativeFailure,
        DataNativeEvidenceContext context,
        string? nativeCode = null);
}

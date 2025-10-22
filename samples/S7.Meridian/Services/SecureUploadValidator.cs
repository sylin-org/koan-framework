using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Koan.Samples.Meridian.Services;

public interface ISecureUploadValidator
{
    Task ValidateAsync(IFormFile file, CancellationToken ct);
}

public sealed class SecureUploadValidator : ISecureUploadValidator
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/plain"
    };

    private static readonly long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
    private static readonly Regex SuspiciousNameRegex = new(@"(\.|%2e){2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task ValidateAsync(IFormFile file, CancellationToken ct)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Uploaded file is empty.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"Uploaded file exceeds {MaxFileSizeBytes / (1024 * 1024)}MB limit.");
        }

        if (!AllowedMimeTypes.Contains(file.ContentType))
        {
            throw new InvalidOperationException($"MIME type '{file.ContentType}' is not permitted.");
        }

        if (SuspiciousNameRegex.IsMatch(file.FileName) || file.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("File name contains suspicious patterns.");
        }

        await using var stream = file.OpenReadStream();
        var buffer = new byte[4];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
        if (read < buffer.Length)
        {
            throw new InvalidOperationException("Unable to inspect uploaded file.");
        }

        var signature = BitConverter.ToString(buffer);
        if (file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            if (!signature.StartsWith("25-50-44-46", StringComparison.Ordinal)) // %PDF
            {
                throw new InvalidOperationException("PDF signature mismatch.");
            }
        }
        else if (file.ContentType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase))
        {
            // DOCX (ZIP) magic number: PK\u0003\u0004
            if (!signature.StartsWith("50-4B-03-04", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("DOCX signature mismatch.");
            }
        }
    }
}

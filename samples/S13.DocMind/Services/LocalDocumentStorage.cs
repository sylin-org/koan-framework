using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public sealed class LocalDocumentStorage : IDocumentStorage
{
    private readonly DocMindOptions _options;
    private readonly ILogger<LocalDocumentStorage> _logger;

    public LocalDocumentStorage(IOptions<DocMindOptions> options, ILogger<LocalDocumentStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StoredDocumentDescriptor> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name required", nameof(fileName));

        var basePath = EnsureBasePath();
        var hashedName = $"{Guid.NewGuid():N}-{Sanitize(fileName)}";
        var destination = Path.Combine(basePath, hashedName);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using var fileStream = File.Create(destination);
        string hash;
        using (var hashAlgorithm = SHA512.Create())
        {
            await using var cryptoStream = new CryptoStream(fileStream, hashAlgorithm, CryptoStreamMode.Write, leaveOpen: true);
            await content.CopyToAsync(cryptoStream, cancellationToken);
            await cryptoStream.FlushAsync(cancellationToken);
            await fileStream.FlushAsync(cancellationToken);
            hash = Convert.ToHexString(hashAlgorithm.Hash ?? Array.Empty<byte>());
        }

        var fileInfo = new FileInfo(destination);
        _logger.LogInformation("Stored document {File} ({Length} bytes) at {Path}", fileName, fileInfo.Length, destination);

        return new StoredDocumentDescriptor
        {
            Provider = "local",
            Bucket = _options.Storage.Bucket,
            ObjectKey = hashedName,
            ProviderPath = destination,
            Length = fileInfo.Length,
            Hash = hash
        };
    }

    public Task<Stream> OpenReadAsync(DocumentStorageLocation location, CancellationToken cancellationToken)
    {
        if (location is null) throw new ArgumentNullException(nameof(location));
        if (!location.TryResolvePhysicalPath(out var path))
        {
            throw new FileNotFoundException("Storage path unavailable for provider", location.ObjectKey);
        }

        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(DocumentStorageLocation location, CancellationToken cancellationToken)
    {
        if (location is null)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(location.TryResolvePhysicalPath(out var path) && File.Exists(path));
    }

    public Task<bool> TryDeleteAsync(DocumentStorageLocation location, CancellationToken cancellationToken)
    {
        if (location is null || !location.TryResolvePhysicalPath(out var path))
        {
            return Task.FromResult(false);
        }

        try
        {
            if (!File.Exists(path))
            {
                return Task.FromResult(false);
            }

            File.Delete(path);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to delete stored document at {Path}", path);
            return Task.FromResult(false);
        }
    }

    private string EnsureBasePath()
    {
        var path = _options.Storage.BasePath;
        if (!Path.IsPathFullyQualified(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[name.Length];
        var index = 0;
        foreach (var c in name)
        {
            if (invalid.Contains(c)) continue;
            buffer[index++] = c == ' ' ? '_' : c;
        }
        return new string(buffer[..index]);
    }
}

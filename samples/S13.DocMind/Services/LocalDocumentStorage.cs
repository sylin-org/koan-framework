using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;

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

    public async Task<StoredDocumentLocation> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name required", nameof(fileName));

        var basePath = EnsureBasePath();
        var hashedName = $"{Guid.NewGuid():N}-{Sanitize(fileName)}";
        var destination = Path.Combine(basePath, hashedName);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using var fileStream = File.Create(destination);
        string hash;
        await using (var hashAlgorithm = SHA512.Create())
        {
            await using var cryptoStream = new CryptoStream(fileStream, hashAlgorithm, CryptoStreamMode.Write, leaveOpen: true);
            await content.CopyToAsync(cryptoStream, cancellationToken);
            await cryptoStream.FlushAsync(cancellationToken);
            await fileStream.FlushAsync(cancellationToken);
            hash = Convert.ToHexString(hashAlgorithm.Hash ?? Array.Empty<byte>());
        }

        var fileInfo = new FileInfo(destination);
        _logger.LogInformation("Stored document {File} ({Length} bytes) at {Path}", fileName, fileInfo.Length, destination);

        return new StoredDocumentLocation
        {
            Provider = "local",
            Path = destination,
            Length = fileInfo.Length,
            Hash = hash
        };
    }

    public Task<Stream> OpenReadAsync(StoredDocumentLocation location, CancellationToken cancellationToken)
    {
        if (location is null) throw new ArgumentNullException(nameof(location));
        Stream stream = File.OpenRead(location.Path);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(StoredDocumentLocation location, CancellationToken cancellationToken)
        => Task.FromResult(File.Exists(location.Path));

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

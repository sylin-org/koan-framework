using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.AI.Connector.HuggingFace.Api;
using Koan.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.AI.Connector.HuggingFace;

/// <summary>
/// Low-level HTTP client for the HuggingFace Hub API.
/// Handles authentication, search, metadata retrieval, file listing, and downloads.
/// </summary>
internal sealed class HuggingFaceClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<HuggingFaceClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HuggingFaceClient(IOptions<HuggingFaceOptions> options, ILogger<HuggingFaceClient> logger)
    {
        _logger = logger;

        var opts = options.Value;
        var token = opts.Token ?? Environment.GetEnvironmentVariable("HF_TOKEN");

        _http = new HttpClient { BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/") };

        if (token is not null)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Koan.AI/0.6.3");
    }

    /// <summary>
    /// Search for models on HuggingFace Hub.
    /// </summary>
    public async Task<IReadOnlyList<HfModelInfo>> SearchAsync(
        string query, int limit, CancellationToken ct)
    {
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"api/models?search={encodedQuery}&limit={limit}&sort=downloads&direction=-1";

        _logger.LogDebug("HuggingFace search: {Url}", url);

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var results = await response.Content.ReadFromJsonAsync<List<HfModelInfo>>(JsonOptions, ct);
        return results ?? [];
    }

    /// <summary>
    /// Get detailed metadata for a specific model.
    /// </summary>
    public async Task<HfModelInfo?> GetModelInfoAsync(string modelId, CancellationToken ct)
    {
        var encodedId = Uri.EscapeDataString(modelId);
        var url = $"api/models/{encodedId}";

        _logger.LogDebug("HuggingFace model info: {ModelId}", modelId);

        var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HfModelInfo>(JsonOptions, ct);
    }

    /// <summary>
    /// List files in a model repository.
    /// </summary>
    public async Task<IReadOnlyList<HfFileInfo>> ListFilesAsync(string modelId, CancellationToken ct)
    {
        var encodedId = Uri.EscapeDataString(modelId);
        var url = $"api/models/{encodedId}/tree/main";

        _logger.LogDebug("HuggingFace file list: {ModelId}", modelId);

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var files = await response.Content.ReadFromJsonAsync<List<HfFileInfo>>(JsonOptions, ct);
        return files ?? [];
    }

    /// <summary>
    /// Download a single file from a model repository with progress reporting.
    /// </summary>
    public async Task DownloadFileAsync(
        string modelId, string fileName, string outputPath,
        IProgress<ModelPullProgress>? progress, CancellationToken ct)
    {
        var url = $"{Uri.EscapeDataString(modelId)}/resolve/main/{fileName}";

        _logger.LogInformation("Downloading {ModelId}/{FileName}", modelId, fileName);

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var directory = Path.GetDirectoryName(outputPath);

        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long bytesDownloaded = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            bytesDownloaded += bytesRead;

            progress?.Report(new ModelPullProgress
            {
                Phase = "Downloading",
                Percent = totalBytes > 0 ? (double)bytesDownloaded / totalBytes.Value * 100 : 0,
                BytesDownloaded = bytesDownloaded,
                TotalBytes = totalBytes
            });
        }

        _logger.LogInformation("Downloaded {ModelId}/{FileName} ({Bytes} bytes)", modelId, fileName, bytesDownloaded);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

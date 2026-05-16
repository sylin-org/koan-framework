using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Koan.AI.Contracts.Options;
using Koan.AI.Context;
using Koan.Data.Core;
using Koan.Storage.Abstractions;
using Koan.Media.Abstractions.Model;

namespace Koan.AI.Pipelines;

/// <summary>
/// Pipeline stage for image-based AI operations.
/// Supports lazy generation from text, analysis, and storage.
/// </summary>
public sealed class ImagePipeline : IAiPipelineStage<byte[]>
{
    private readonly byte[]? _imageBytes;
    private readonly string? _textInput;
    private readonly string? _mimeType;
    private readonly PipelineContext _context;
    private readonly Lazy<Task<byte[]>>? _lazyGeneration;

    /// <summary>
    /// Create image pipeline from existing image bytes.
    /// </summary>
    internal ImagePipeline(byte[] bytes, string? mimeType, PipelineContext context)
    {
        _imageBytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        _mimeType = mimeType ?? "image/jpeg";
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Create image pipeline from text (lazy generation).
    /// </summary>
    internal ImagePipeline(string textInput, PipelineContext context)
    {
        if (string.IsNullOrWhiteSpace(textInput))
            throw new ArgumentException("Text input cannot be null or empty", nameof(textInput));

        _textInput = textInput;
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _mimeType = "image/png";  // Default for generated images

        // Lazy generation - cached for multiple terminal operations
        _lazyGeneration = new Lazy<Task<byte[]>>(async () =>
        {
            using (_context.Model != null || _context.Source != null
                ? Client.Scope(all: _context.Source)
                : null)
            {
                throw new InvalidOperationException(
                    "Text-to-image generation requires an adapter that supports image generation " +
                    "(e.g., DALL-E, Stable Diffusion). No image generation adapter is currently registered.");
            }
        });
    }

    /// <summary>
    /// Save image to storage using entity-first pattern (recommended).
    /// Storage profile and container determined by [StorageBinding] attribute on TEntity.
    /// Terminal operation - executes pipeline and saves.
    /// </summary>
    /// <typeparam name="TEntity">Entity type with [StorageBinding] attribute</typeparam>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Strongly-typed entity with metadata populated</returns>
    /// <example>
    /// <code>
    /// [StorageBinding(Profile = "ai-generated", Container = "images")]
    /// public class GeneratedImage : MediaEntity&lt;GeneratedImage&gt; { }
    ///
    /// var image = await Ai.FromText("A sunset over mountains")
    ///     .ToImage(model: "dall-e-3")
    ///     .ToStorage&lt;GeneratedImage&gt;();
    ///
    /// Console.WriteLine($"Saved: {image.Id}, Size: {image.Size} bytes");
    /// </code>
    /// </example>
    public async Task<TEntity> ToStorage<TEntity>(CancellationToken ct = default)
        where TEntity : MediaEntity<TEntity>, new()
    {
        var bytes = _imageBytes ?? await _lazyGeneration!.Value;
        var extension = GetFileExtension(_mimeType);
        var filename = $"ai-gen-{Guid.CreateVersion7()}.{extension}";

        using var stream = new MemoryStream(bytes);
        var storageObject = await UploadToStorage<TEntity>(stream, filename, _mimeType ?? "image/png", ct);

        // Map storage object to entity
        var entity = MapToEntity<TEntity>(storageObject);

        // Save entity metadata to database
        await entity.Save(ct);

        return entity;
    }

    /// <summary>
    /// Save image to storage with custom filename.
    /// Combines type safety with filename control.
    /// Terminal operation - executes pipeline and saves.
    /// </summary>
    /// <typeparam name="TEntity">Entity type with [StorageBinding] attribute</typeparam>
    /// <param name="filename">Custom filename (e.g., "logo.png"). Auto-generated if null.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Strongly-typed entity with metadata populated</returns>
    /// <example>
    /// <code>
    /// var logo = await Ai.FromText("Modern tech logo")
    ///     .ToImage()
    ///     .ToStorage&lt;GeneratedImage&gt;("acme-corp-logo.png");
    /// </code>
    /// </example>
    public async Task<TEntity> ToStorage<TEntity>(
        string? filename,
        CancellationToken ct = default)
        where TEntity : MediaEntity<TEntity>, new()
    {
        var bytes = _imageBytes ?? await _lazyGeneration!.Value;

        // Auto-generate filename if not provided
        if (string.IsNullOrWhiteSpace(filename))
        {
            var extension = GetFileExtension(_mimeType);
            filename = $"ai-gen-{Guid.CreateVersion7()}.{extension}";
        }

        using var stream = new MemoryStream(bytes);
        var storageObject = await UploadToStorage<TEntity>(stream, filename, _mimeType ?? "image/png", ct);

        // Map storage object to entity
        var entity = MapToEntity<TEntity>(storageObject);

        // Save entity metadata to database
        await entity.Save(ct);

        return entity;
    }

    /// <summary>
    /// Save image to storage with explicit profile and container routing.
    /// Provides full control over storage location for scripting scenarios.
    /// Terminal operation - executes pipeline and saves.
    /// </summary>
    /// <param name="profile">Storage profile (e.g., "hot", "cold", "ai-generated"). Uses default if null.</param>
    /// <param name="container">Storage container/bucket. Uses default if null.</param>
    /// <param name="key">Storage key/path. Auto-generated if null.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Storage object with metadata</returns>
    /// <example>
    /// <code>
    /// var result = await Ai.FromText("Generate a logo")
    ///     .ToImage()
    ///     .ToStorage(
    ///         profile: "ai-generated",
    ///         container: "logos",
    ///         key: "company-logo.png"
    ///     );
    ///
    /// Console.WriteLine($"Stored at: {result.Key}");
    /// </code>
    /// </example>
    public async Task<IStorageObject> ToStorage(
        string? profile = null,
        string? container = null,
        string? key = null,
        CancellationToken ct = default)
    {
        var bytes = _imageBytes ?? await _lazyGeneration!.Value;
        var storageService = GetStorageService();

        // Auto-generate key if not provided
        if (string.IsNullOrWhiteSpace(key))
        {
            var extension = GetFileExtension(_mimeType);
            key = $"ai-gen-{Guid.CreateVersion7()}.{extension}";
        }

        using var stream = new MemoryStream(bytes);
        return await storageService.Put(
            profile ?? "",
            container ?? "",
            key,
            stream,
            _mimeType ?? "image/png",
            ct
        );
    }

    /// <summary>
    /// Understand/analyze image with AI vision.
    /// Terminal operation - executes immediately.
    /// </summary>
    /// <param name="prompt">Question or instruction about the image</param>
    /// <param name="model">Optional model override</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>AI response text</returns>
    public async Task<string> ToText(string prompt, string? model = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));

        var bytes = _imageBytes ?? await _lazyGeneration!.Value;

        using (_context.Model != null || model != null || _context.Source != null
            ? Client.Scope(all: _context.Source)
            : null)
        {
            return await Client.Chat(prompt, new ChatOptions
            {
                Image = bytes,
                Model = model ?? _context.Model
            }, ct);
        }
    }

    /// <summary>
    /// Get raw image bytes.
    /// Terminal operation - executes if generated from text.
    /// </summary>
    public async Task<byte[]> ToBytes(CancellationToken ct = default)
    {
        return _imageBytes ?? await _lazyGeneration!.Value;
    }

    /// <summary>
    /// Execute pipeline and return image bytes.
    /// </summary>
    public Task<byte[]> Execute(CancellationToken ct = default)
        => ToBytes(ct);

    /// <summary>
    /// Stream image bytes as single item.
    /// </summary>
    public async IAsyncEnumerable<byte[]> Stream([EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return await Execute(ct);
    }

    // ============================================================================
    // Helper Methods
    // ============================================================================

    private static string GetFileExtension(string? mimeType) => mimeType switch
    {
        "image/png" => "png",
        "image/jpeg" or "image/jpg" => "jpg",
        "image/webp" => "webp",
        "image/gif" => "gif",
        "image/bmp" => "bmp",
        "image/svg+xml" => "svg",
        _ => "bin"
    };

    private static IStorageService GetStorageService()
    {
        var sp = Koan.Core.Hosting.App.AppHost.Current
            ?? throw new InvalidOperationException(
                "AppHost.Current not set. Call services.AddKoan() in Program.cs.");

        return sp.GetService(typeof(IStorageService)) as IStorageService
            ?? throw new InvalidOperationException(
                "IStorageService not registered. Add Koan.Storage package and call services.AddStorage().");
    }

    private static async Task<IStorageObject> UploadToStorage<TEntity>(
        Stream content,
        string filename,
        string contentType,
        CancellationToken ct)
        where TEntity : class, IStorageObject
    {
        var storageService = GetStorageService();
        var (profile, container) = ResolveStorageBinding<TEntity>();

        return await storageService.Put(
            profile,
            container,
            filename,
            content,
            contentType,
            ct
        );
    }

    private static (string Profile, string Container) ResolveStorageBinding<TEntity>()
        where TEntity : class
    {
        var type = typeof(TEntity);
        var attr = type.GetCustomAttributes(typeof(Koan.Storage.Infrastructure.StorageBindingAttribute), inherit: false)
            .OfType<Koan.Storage.Infrastructure.StorageBindingAttribute>()
            .FirstOrDefault();

        return (attr?.Profile ?? "", attr?.Container ?? "");
    }

    private static TEntity MapToEntity<TEntity>(IStorageObject storageObject)
        where TEntity : class, IStorageObject, new()
    {
        var entity = new TEntity();

        // Map storage metadata to entity properties
        if (entity is Koan.Storage.Model.StorageEntity<TEntity> se)
        {
            se.Id = storageObject.Id;
            se.Key = storageObject.Key;
            se.Name = storageObject.Name;
            se.ContentType = storageObject.ContentType;
            se.Size = storageObject.Size;
            se.ContentHash = storageObject.ContentHash;
            se.CreatedAt = storageObject.CreatedAt;
            se.UpdatedAt = storageObject.UpdatedAt;
            se.Provider = storageObject.Provider;
            se.Container = storageObject.Container;
            se.Tags = storageObject.Tags;
        }

        return entity;
    }
}

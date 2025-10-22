---
type: DEV
domain: media
title: "Fluent Media Transformation Pipeline API"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-10-16
framework_version: v0.6.3
related_adrs: [DX-0046]
---

# DX-0047: Fluent Media Transformation Pipeline API

Status: Approved

## Context

The framework currently provides **MediaOperators** (ResizeOperator, RotateOperator, TypeConverterOperator) that require **imperative execution loops**:

```csharp
// Current pattern: Manual operator chaining
Stream current = sourceStream;
foreach (var (op, pars) in operators)
{
    var next = new MemoryStream();
    var result = await op.Execute(current, contentType, next, pars, options, ct);
    next.Position = 0;
    if (!ReferenceEquals(current, sourceStream)) current.Dispose();
    current = next;
    contentType = result.ContentType ?? contentType;
}
```

**Problems**:
1. **Poor Developer Experience**: Verbose, error-prone boilerplate in every controller
2. **No Composition**: Can't build reusable transformation pipelines
3. **Unclear Intent**: Loops obscure the transformation sequence
4. **Stream Management**: Manual disposal logic in every implementation

**URL-based transformations** provide simple client API but lack programmatic flexibility:
```
GET /media/{id}/photo.jpg?w=300&h=300&fit=cover
```

**Goal**: Provide fluent, declarative API for server-side transformation pipelines while maintaining URL-based client API for HTTP caching.

---

## Decision

Implement **fluent transformation API** as extension methods on `Stream` with:
- **Immediate execution** (early failure, clear stack traces)
- **Type-based overloading** (`int` for pixels, `double` for scale factors)
- **Automatic stream disposal** (clean resource management)
- **Materialization pattern** (`.Result()` for branching/reuse)
- **Entity integration** (`.StoreAs<T>()` creates MediaEntity derivatives)

**Hybrid approach**:
- **Client-driven transforms**: URL-based (`?w=300&h=300`)
- **Server-side pipelines**: Fluent API (developer experience)
- **Unified caching**: Transform signature maps to cached variants

---

## API Design

### Core Operations

#### 1. Resize Operations

```csharp
// ABSOLUTE RESIZE (int = pixels)

/// <summary>
/// Resize to exact dimensions (may distort aspect ratio)
/// Example: Resize(300, 200) → 300×200 regardless of original aspect
/// </summary>
Task<Stream> Resize(int width, int height, CancellationToken ct = default);

/// <summary>
/// Resize to fit within dimensions (preserves aspect ratio, no cropping)
/// Example: 1000×500 → ResizeFit(800, 600) → 800×400 (fits within bounds)
/// </summary>
Task<Stream> ResizeFit(int maxWidth, int maxHeight, CancellationToken ct = default);

/// <summary>
/// Resize to cover dimensions (preserves aspect ratio, crops overflow)
/// Example: 1000×500 → ResizeCover(800, 800) → 800×800 (crops sides)
/// </summary>
Task<Stream> ResizeCover(int width, int height, CropFrom anchor = CropFrom.Center, CancellationToken ct = default);

/// <summary>
/// Resize by width, calculate height to preserve aspect ratio
/// Example: 1000×500 → ResizeX(500) → 500×250 (factor: 0.5, applied to both axes)
/// </summary>
Task<Stream> ResizeX(int targetWidth, CancellationToken ct = default);

/// <summary>
/// Resize by height, calculate width to preserve aspect ratio
/// Example: 1000×500 → ResizeY(250) → 500×250 (factor: 0.5, applied to both axes)
/// </summary>
Task<Stream> ResizeY(int targetHeight, CancellationToken ct = default);

// RELATIVE RESIZE (double = scale factors)

/// <summary>
/// Resize by scale factors (double for relative)
/// Example: Resize(0.5, 0.5) → 50% width × 50% height
/// Example: Resize(0.5, 1.0) → 50% width × 100% height (skinny)
/// </summary>
Task<Stream> Resize(double scaleX, double scaleY, CancellationToken ct = default);
```

#### 2. Crop Operations

```csharp
/// <summary>
/// Crop to absolute pixel coordinates
/// Example: Crop(100, 100, 300, 200) → 300×200 region starting at (100,100)
/// </summary>
Task<Stream> Crop(int x, int y, int width, int height, CancellationToken ct = default);

/// <summary>
/// Crop to rectangle
/// </summary>
Task<Stream> Crop(Rectangle rect, CancellationToken ct = default);

/// <summary>
/// Crop centered region
/// Example: 1000×500 → CropCenter(400, 400) → 400×400 square from center
/// </summary>
Task<Stream> CropCenter(int width, int height, CancellationToken ct = default);

/// <summary>
/// Crop to aspect ratio (e.g., 16:9, 4:3, 1:1)
/// Example: 1000×500 → Crop(1.0) → 500×500 (square from center)
/// Example: 1000×500 → Crop(16.0/9.0) → 1000×562.5 (16:9 from center)
/// Example: 1000×1000 → Crop(16.0/9.0, CropFrom.Top) → 1000×562.5 (16:9 from top)
/// </summary>
Task<Stream> Crop(double aspectRatio, CropFrom anchor = CropFrom.Center, CancellationToken ct = default);

/// <summary>
/// Crop to square (convenience for 1:1 aspect ratio)
/// Equivalent to: Crop(1.0, anchor)
/// Example: 1000×500 → CropSquare() → 500×500 from center
/// </summary>
Task<Stream> CropSquare(CropFrom anchor = CropFrom.Center, CancellationToken ct = default);
```

#### 3. Padding Operations

```csharp
/// <summary>
/// Pad to exact dimensions with color fill
/// Example: 500×500 → Pad(800, 600, Color.Black) → 800×600 with black bars
/// </summary>
Task<Stream> Pad(int width, int height, Color? color = null, CancellationToken ct = default);

/// <summary>
/// Pad to constraint (e.g., square, 16:9 letterbox)
/// Example: 1000×500 → Pad(PadTo.Square, Color.Black) → 1000×1000 with black bars
/// </summary>
Task<Stream> Pad(PadTo constraint, Color? color = null, CancellationToken ct = default);

public enum PadTo
{
    Square,           // 1:1
    Landscape_16_9,   // 16:9 letterbox
    Portrait_9_16,    // 9:16 pillarbox
    Landscape_4_3,    // 4:3 letterbox
    Portrait_3_4,     // 3:4 pillarbox
    Landscape_21_9    // 21:9 ultrawide
}
```

#### 4. Rotation & Orientation

```csharp
/// <summary>
/// Auto-orient using EXIF metadata
/// </summary>
Task<Stream> AutoOrient(CancellationToken ct = default);

/// <summary>
/// Rotate by degrees (clockwise)
/// Example: Rotate(90) → 90° clockwise rotation
/// </summary>
Task<Stream> Rotate(int degrees, CancellationToken ct = default);

/// <summary>
/// Flip horizontally (mirror)
/// </summary>
Task<Stream> FlipHorizontal(CancellationToken ct = default);

/// <summary>
/// Flip vertically
/// </summary>
Task<Stream> FlipVertical(CancellationToken ct = default);
```

#### 5. Format & Quality

```csharp
/// <summary>
/// Convert image format
/// Example: ConvertFormat("webp", 85)
/// </summary>
Task<Stream> ConvertFormat(string format, int quality = 85, CancellationToken ct = default);

/// <summary>
/// Optimize quality without format conversion
/// </summary>
Task<Stream> OptimizeQuality(int quality, CancellationToken ct = default);
```

#### 6. Materialization & Storage

```csharp
/// <summary>
/// Materialize transformation result for branching/reuse
/// Returns TransformResult that can be branched multiple times
/// </summary>
Task<TransformResult> Result(CancellationToken ct = default);

/// <summary>
/// Store transformed stream as new MediaEntity
/// Automatically disposes stream after upload
/// </summary>
Task<string> StoreAs<TEntity>(CancellationToken ct = default)
    where TEntity : MediaEntity<TEntity>, new();

/// <summary>
/// Get transformed stream as byte array (for in-memory processing)
/// </summary>
Task<byte[]> ToBytes(CancellationToken ct = default);

/// <summary>
/// Get transformed stream as base64 string (for embedding)
/// </summary>
Task<string> ToBase64(CancellationToken ct = default);
```

---

## Supporting Types

### CropFrom Enum
```csharp
public enum CropFrom
{
    Center,
    Top,
    Bottom,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}
```

### Rectangle Struct
```csharp
public readonly struct Rectangle
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public Rectangle(int x, int y, int width, int height)
    {
        X = x; Y = y; Width = width; Height = height;
    }
}
```

### TransformResult Class
```csharp
/// <summary>
/// Materialized transformation result that can be branched for multiple derivatives
/// </summary>
public sealed class TransformResult : IAsyncDisposable
{
    private readonly MemoryStream _materializedStream;
    private readonly string _contentType;

    internal TransformResult(Stream source, string contentType)
    {
        _materializedStream = new MemoryStream();
        source.CopyTo(_materializedStream);
        source.Dispose();  // Dispose source after materialization
        _materializedStream.Position = 0;
        _contentType = contentType;
    }

    /// <summary>
    /// Create independent branch from materialized result
    /// </summary>
    public Stream Branch()
    {
        _materializedStream.Position = 0;
        var clone = new MemoryStream();
        _materializedStream.CopyTo(clone);
        clone.Position = 0;
        return clone;
    }

    public async ValueTask DisposeAsync()
    {
        await _materializedStream.DisposeAsync();
    }
}
```

---

## Usage Examples

### Example 1: Basic Transformation
```csharp
// Simple thumbnail generation
var thumbnailId = await photo
    .OpenRead()
    .AutoOrient()
    .ResizeX(150)
    .StoreAs<PhotoThumbnail>();
```

### Example 2: Relative Resize (Skinny Image)
```csharp
// Create skinny version (50% width, 100% height)
var skinny = await photo
    .OpenRead()
    .Resize(0.5, 1.0)  // double = scale factors
    .StoreAs<SkinnyPhoto>();
```

### Example 3: Single-Axis Aspect-Preserving Resize
```csharp
// Responsive images with consistent width
var mobile = await photo.OpenRead().ResizeX(375);   // iPhone width, height scales
var tablet = await photo.OpenRead().ResizeX(768);   // iPad width, height scales
var desktop = await photo.OpenRead().ResizeX(1920); // Desktop width, height scales

// Store each
await mobile.StoreAs<MobilePhoto>();
await tablet.StoreAs<TabletPhoto>();
await desktop.StoreAs<DesktopPhoto>();
```

### Example 4: Crop to Aspect Ratio
```csharp
// Create square profile photo from portrait
var profile = await photo
    .OpenRead()
    .CropSquare(CropFrom.Top)      // Square crop from top (preserve face)
    .Resize(300, 300)              // Exact 300×300
    .StoreAs<ProfilePhoto>();

// Create 16:9 hero image
var hero = await photo
    .OpenRead()
    .Crop(16.0 / 9.0, CropFrom.Center)  // 16:9 aspect - simplified API!
    .ResizeX(1920)                       // 1920px wide
    .OptimizeQuality(80)
    .ConvertFormat("webp")
    .StoreAs<HeroImage>();
```

### Example 5: Manual Crop Coordinates
```csharp
// Extract specific region (face detection coordinates)
var face = await photo
    .OpenRead()
    .Crop(x: 250, y: 100, width: 400, height: 400)  // Face bounding box
    .Resize(150, 150)
    .StoreAs<FaceThumbnail>();
```

### Example 6: Branching with .Result()
```csharp
// Shared preprocessing, multiple derivatives
using var result = await photo
    .OpenRead()
    .AutoOrient()
    .OptimizeQuality(85)
    .Result();  // Materialize for branching

// Create multiple derivatives efficiently
var small = await result.Branch().ResizeX(150).StoreAs<ThumbnailSmall>();
var medium = await result.Branch().ResizeX(300).StoreAs<ThumbnailMedium>();
var large = await result.Branch().ResizeX(600).StoreAs<ThumbnailLarge>();
var square = await result.Branch().CropSquare().Resize(400, 400).StoreAs<SquareThumbnail>();
```

### Example 7: Complex Pipeline
```csharp
// Full processing pipeline
var processed = await photo
    .OpenRead()
    .AutoOrient()                           // Fix EXIF rotation
    .Crop(16.0 / 9.0, CropFrom.Center)     // Crop to 16:9 - simplified!
    .ResizeX(1920)                          // Max width 1920
    .Pad(PadTo.Landscape_16_9, Color.Black) // Ensure 16:9 with letterbox
    .OptimizeQuality(80)                    // Reduce quality
    .ConvertFormat("webp", 85)              // Convert to WebP
    .StoreAs<ProcessedPhoto>();
```

### Example 8: In-Memory Processing
```csharp
// Get bytes for external API
var bytes = await photo
    .OpenRead()
    .ResizeX(512)
    .ConvertFormat("png")
    .ToBytes();  // Returns byte[] instead of storing

// Send to AI vision service
var analysis = await _visionService.AnalyzeAsync(bytes);

// Or get base64 for embedding
var base64 = await photo.OpenRead().ResizeX(100).ToBase64();
var dataUrl = $"data:image/jpeg;base64,{base64}";
```

---

## Implementation Strategy

### Phase 1: Core Extension Methods

**Location**: `src/Koan.Media.Core/Extensions/StreamTransformExtensions.cs`

**Dependencies**:
- ImageSharp (already in framework via MediaOperators)
- Existing MediaOperator infrastructure (ResizeOperator, RotateOperator, etc.)

**Implementation pattern**:
```csharp
public static class StreamTransformExtensions
{
    // Extension methods call into MediaOperators
    public static async Task<Stream> Resize(this Stream source, int width, int height, CancellationToken ct = default)
    {
        try
        {
            using var image = await Image.LoadAsync(source, ct);
            image.Mutate(x => x.Resize(width, height));

            var result = new MemoryStream();
            await image.SaveAsync(result, new JpegEncoder(), ct);
            result.Position = 0;

            return result;
        }
        finally
        {
            await source.DisposeAsync();  // Always dispose input stream
        }
    }

    public static async Task<Stream> ResizeX(this Stream source, int targetWidth, CancellationToken ct)
    {
        // Peek dimensions without loading full image
        source.Position = 0;
        var info = await Image.IdentifyAsync(source, ct);  // Fast, header-only read
        source.Position = 0;

        var scaleFactor = targetWidth / (double)info.Width;
        var targetHeight = (int)(info.Height * scaleFactor);

        // Delegate to standard resize
        return await source.Resize(targetWidth, targetHeight, ct);
    }

    // Similar pattern for all operations...
}
```

### Phase 2: TransformResult & Branching

**Location**: `src/Koan.Media.Core/Models/TransformResult.cs`

```csharp
public sealed class TransformResult : IAsyncDisposable
{
    private readonly MemoryStream _stream;
    private readonly string _contentType;

    public Stream Branch() { /* Clone stream */ }
    public async ValueTask DisposeAsync() { /* Cleanup */ }
}

// Extension
public static async Task<TransformResult> Result(this Stream source, CancellationToken ct)
{
    return new TransformResult(source, "image/jpeg");  // Detect content type
}
```

### Phase 3: Entity Integration

**Location**: `src/Koan.Media.Abstractions/Extensions/MediaEntityTransformExtensions.cs`

```csharp
public static class MediaEntityTransformExtensions
{
    public static async Task<string> StoreAs<TEntity>(this Stream source, CancellationToken ct)
        where TEntity : MediaEntity<TEntity>, new()
    {
        try
        {
            var entity = await TEntity.Onboard(
                name: $"derived-{Guid.NewGuid()}.bin",
                content: source,
                contentType: "image/jpeg",  // Detect from stream
                ct: ct
            );

            await entity.Save();
            return entity.Id;
        }
        finally
        {
            await source.DisposeAsync();
        }
    }
}
```

### Phase 4: Hybrid URL-Based Caching

**Location**: `samples/S6.SnapVault/Controllers/MediaController.cs` (sample implementation)

```csharp
[HttpGet("media/{id}/{filename}")]
public async Task<IActionResult> GetTransformed(string id, string filename)
{
    var photo = await PhotoAsset.Get(id);
    var transformParams = Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString());

    if (transformParams.Count == 0)
        return Redirect(await GetPresignedUrl(photo));

    // Compute transformation signature
    var signature = ComputeSignature(photo.Key, transformParams);
    var cacheKey = $"transform:{id}:{signature}";

    // Check cache
    var variantId = await _cache.GetStringAsync(cacheKey);
    if (variantId != null)
    {
        var variant = await PhotoVariant.Get(variantId);
        return Redirect(await GetPresignedUrl(variant));
    }

    // Execute transformation using fluent API
    var transformedStream = await ApplyFluentTransform(photo, transformParams);

    // Store and cache
    var variant = await transformedStream.StoreAs<PhotoVariant>();
    variant.SourceMediaId = photo.Id;
    variant.DerivationKey = signature;
    await variant.Save();

    await _cache.SetStringAsync(cacheKey, variant.Id, TimeSpan.FromDays(30));

    return Redirect(await GetPresignedUrl(variant));
}

private async Task<Stream> ApplyFluentTransform(PhotoAsset photo, Dictionary<string, string> p)
{
    var stream = await photo.OpenRead();

    // Use fluent API based on parameters
    if (p.TryGetValue("w", out var w) && p.TryGetValue("h", out var h))
        stream = await stream.Resize(int.Parse(w), int.Parse(h));

    if (p.TryGetValue("crop", out var crop) && crop == "square")
        stream = await stream.CropSquare();

    if (p.TryGetValue("format", out var format))
        stream = await stream.ConvertFormat(format);

    return stream;
}
```

---

## Testing Strategy

### Unit Tests

**Location**: `tests/Koan.Media.Core.Tests/Extensions/StreamTransformExtensionsTests.cs`

```csharp
public class StreamTransformExtensionsTests
{
    [Fact]
    public async Task Resize_AbsoluteDimensions_ProducesCorrectSize()
    {
        // Arrange
        var sourceStream = LoadTestImage("1000x500.jpg");  // 1000×500

        // Act
        var result = await sourceStream.Resize(300, 200);
        using var image = await Image.LoadAsync(result);

        // Assert
        Assert.Equal(300, image.Width);
        Assert.Equal(200, image.Height);
    }

    [Fact]
    public async Task Resize_RelativeScale_AppliesScaleFactor()
    {
        var sourceStream = LoadTestImage("1000x500.jpg");

        var result = await sourceStream.Resize(0.5, 0.5);
        using var image = await Image.LoadAsync(result);

        Assert.Equal(500, image.Width);
        Assert.Equal(250, image.Height);
    }

    [Fact]
    public async Task ResizeX_PreservesAspectRatio()
    {
        var sourceStream = LoadTestImage("1000x500.jpg");

        var result = await sourceStream.ResizeX(500);  // 50% of X
        using var image = await Image.LoadAsync(result);

        Assert.Equal(500, image.Width);
        Assert.Equal(250, image.Height);  // Y also scaled by 50%
    }

    [Fact]
    public async Task CropSquare_ProducesSquareImage()
    {
        var sourceStream = LoadTestImage("1000x500.jpg");

        var result = await sourceStream.CropSquare();
        using var image = await Image.LoadAsync(result);

        Assert.Equal(500, image.Width);
        Assert.Equal(500, image.Height);
    }

    [Fact]
    public async Task CropToAspectRatio_16_9_ProducesCorrectAspect()
    {
        var sourceStream = LoadTestImage("1000x1000.jpg");

        var result = await sourceStream.CropToAspectRatio(16.0 / 9.0);
        using var image = await Image.LoadAsync(result);

        var aspectRatio = image.Width / (double)image.Height;
        Assert.Equal(16.0 / 9.0, aspectRatio, precision: 2);
    }

    [Fact]
    public async Task Result_AllowsBranching()
    {
        var sourceStream = LoadTestImage("1000x500.jpg");

        using var result = await sourceStream.Resize(0.5, 0.5).Result();

        var branch1 = await result.Branch().Resize(100, 100);
        var branch2 = await result.Branch().Resize(200, 200);

        using var image1 = await Image.LoadAsync(branch1);
        using var image2 = await Image.LoadAsync(branch2);

        Assert.Equal(100, image1.Width);
        Assert.Equal(200, image2.Width);
    }

    [Fact]
    public async Task Chaining_DisposesIntermediateStreams()
    {
        // Test that intermediate streams are properly disposed
        var tracker = new StreamDisposalTracker();
        var sourceStream = tracker.Track(LoadTestImage("test.jpg"));

        var result = await sourceStream
            .Resize(500, 500)
            .CropSquare()
            .OptimizeQuality(80);

        // Verify intermediate streams were disposed
        Assert.True(tracker.AllDisposed());
    }
}
```

### Integration Tests

**Location**: `tests/Koan.Media.Core.Tests/Integration/FluentPipelineIntegrationTests.cs`

```csharp
public class FluentPipelineIntegrationTests
{
    [Fact]
    public async Task ComplexPipeline_ProducesExpectedResult()
    {
        var photo = await TestPhotoAsset.Create();

        var processed = await photo
            .OpenRead()
            .AutoOrient()
            .CropToAspectRatio(16.0 / 9.0)
            .ResizeX(1920)
            .OptimizeQuality(80)
            .ConvertFormat("webp")
            .StoreAs<ProcessedPhoto>();

        var result = await ProcessedPhoto.Get(processed);

        Assert.NotNull(result);
        Assert.Equal("image/webp", result.ContentType);

        using var image = await Image.LoadAsync(await result.OpenRead());
        Assert.Equal(1920, image.Width);
        Assert.Equal(16.0 / 9.0, image.Width / (double)image.Height, precision: 2);
    }
}
```

---

## Performance Considerations

### Memory Management

**Challenge**: Chained transformations create multiple intermediate streams.

**Solution**: Each transformation disposes input stream after processing:
```csharp
try
{
    var result = Transform(source);
    return result;
}
finally
{
    await source.DisposeAsync();  // Always clean up
}
```

### Large Image Optimization

**Challenge**: Loading full images for dimension detection.

**Solution**: Use ImageSharp's `Image.IdentifyAsync()` for header-only reads:
```csharp
var info = await Image.IdentifyAsync(stream);  // Fast, no pixel data loaded
```

### Branching Efficiency

**Challenge**: `.Result()` materializes stream into memory.

**Solution**: Only use `.Result()` when branching is needed. For single-path transformations, stream directly:
```csharp
// No branching → stream directly (memory-efficient)
var thumb = await photo.OpenRead().ResizeX(150).StoreAs<Thumbnail>();

// Branching → materialize once, clone efficiently
using var result = await photo.OpenRead().AutoOrient().Result();
var small = await result.Branch().ResizeX(150).StoreAs<ThumbnailSmall>();
var large = await result.Branch().ResizeX(600).StoreAs<ThumbnailLarge>();
```

---

## Migration Path

### Existing Code (Manual Operators)

```csharp
// Before: Manual operator execution
var sourceStream = await photo.OpenRead();

var orientedStream = await _rotateOperator.ExecuteAsync(
    sourceStream, photo.ContentType, new MemoryStream(),
    new Dictionary<string, string> { ["exif"] = "true" },
    _options, ct);

orientedStream.Position = 0;
var resizedStream = await _resizeOperator.ExecuteAsync(
    orientedStream, photo.ContentType, new MemoryStream(),
    new Dictionary<string, string> { ["w"] = "150", ["h"] = "150", ["fit"] = "crop" },
    _options, ct);

var thumbnail = await PhotoThumbnail.Upload(resizedStream, "thumb.jpg", "image/jpeg");
thumbnail.SourceMediaId = photo.Id;
await thumbnail.Save();
```

### New Code (Fluent API)

```csharp
// After: Fluent API
var thumbnailId = await photo
    .OpenRead()
    .AutoOrient()
    .ResizeCover(150, 150, CropFrom.Center)
    .StoreAs<PhotoThumbnail>();

// Optionally set relationship manually
photo.ThumbnailMediaId = thumbnailId;
await photo.Save();
```

**Migration benefits**:
- 80% less code
- Clear transformation intent
- Automatic resource cleanup
- Type-safe parameters

---

## Alternatives Considered

### Alternative 1: Deferred Execution (Builder Pattern)

```csharp
// Build pipeline (no execution)
var pipeline = photo
    .Transform()
    .Resize(300, 300)
    .CropSquare();

// Execute
var result = await pipeline.Execute();
```

**Rejected**: Deferred execution complicates debugging (no clear failure point) and requires builder state management.

### Alternative 2: Explicit Methods for All Combinations

```csharp
.ResizeAbsolute(300, 300)
.ResizeRelative(0.5, 0.5)
.ResizePercent(50, 50)
```

**Rejected**: Verbose API with too many methods. Type-based overloading (`int` vs `double`) is more idiomatic C#.

### Alternative 3: Parameter Objects

```csharp
.Resize(new ResizeOptions { Width = 300, Height = 300, Mode = ResizeMode.Exact })
```

**Rejected**: More verbose than needed for common cases. Fluent methods are more discoverable.

---

## Success Criteria

- ✅ All resize operations implemented (absolute, relative, single-axis)
- ✅ All crop operations implemented (coordinates, aspect ratio, square)
- ✅ Padding, rotation, format conversion operations
- ✅ `.Result()` materialization for branching
- ✅ `.StoreAs<T>()` entity integration
- ✅ 90%+ unit test coverage
- ✅ Integration tests with real images
- ✅ Performance benchmarks (< 100ms for 1920×1080 resize)
- ✅ Used successfully in S6.SnapVault sample

---

## Implementation Timeline

**Week 1**: Core extension methods (resize, crop, rotate)
**Week 1**: TransformResult & branching
**Week 1**: Unit tests (90% coverage)
**Week 2**: Entity integration (.StoreAs<T>)
**Week 2**: Integration tests & performance benchmarks
**Week 2**: S6.SnapVault integration

**Total**: 1-2 weeks

---

## References

- **ImageSharp Documentation**: https://docs.sixlabors.com/articles/imagesharp/
- **DX-0046**: S6.SnapVault Feature Specification
- **Existing MediaOperators**: `src/Koan.Media.Core/Operators/`

---

**Status**: Approved for implementation
**Implementation Start**: 2025-10-16
**Target Completion**: 2025-10-30
**Framework Version**: v0.6.3+

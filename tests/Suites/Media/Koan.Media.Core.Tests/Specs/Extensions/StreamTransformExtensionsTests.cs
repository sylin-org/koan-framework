using Koan.Media.Core.Extensions;
using Koan.Media.Core.Model;
using Koan.Media.Core.Tests.Support;
using FluentAssertions;
using SixLabors.ImageSharp;

namespace Koan.Media.Core.Tests.Specs.Extensions;

public class StreamTransformExtensionsTests
{
    // ======================================
    // RESIZE TESTS
    // ======================================

    [Fact]
    public async Task Resize_AbsoluteDimensions_ProducesCorrectSize()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.Resize(300, 200);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(300);
        height.Should().Be(200);
    }

    [Fact]
    public async Task Resize_RelativeScale_AppliesScaleFactor()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.Resize(0.5, 0.5);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(500);
        height.Should().Be(250);
    }

    [Fact]
    public async Task Resize_NonUniformScale_CreatesSkinnyImage()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.Resize(0.5, 1.0);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(500);  // 50% of 1000
        height.Should().Be(500); // 100% of 500
    }

    [Fact]
    public async Task ResizeX_PreservesAspectRatio()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.ResizeX(500);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(500);
        height.Should().Be(250);  // 50% scale factor applied to both axes
    }

    [Fact]
    public async Task ResizeY_PreservesAspectRatio()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.ResizeY(250);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(500);  // 50% scale factor applied to both axes
        height.Should().Be(250);
    }

    [Fact]
    public async Task ResizeFit_FitsWithinBounds()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.ResizeFit(800, 600);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(800);  // Constrained by width
        height.Should().Be(400); // Scaled proportionally
    }

    [Fact]
    public async Task ResizeCover_CoversDimensions()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.ResizeCover(800, 800);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(800);
        height.Should().Be(800);
    }

    // ======================================
    // CROP TESTS
    // ======================================

    [Fact]
    public async Task Crop_AbsoluteCoordinates_ProducesCorrectRegion()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.Crop(100, 100, 300, 200);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(300);
        height.Should().Be(200);
    }

    [Fact]
    public async Task Crop_Rectangle_ProducesCorrectRegion()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);
        var rect = new Koan.Media.Core.Model.Rectangle(50, 50, 400, 300);

        // Act
        var result = await sourceStream.Crop(rect);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(400);
        height.Should().Be(300);
    }

    [Fact]
    public async Task CropCenter_CreatescenteredRegion()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.CropCenter(400, 400);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(400);
        height.Should().Be(400);
    }

    [Fact]
    public async Task CropSquare_ProducesSquareImage()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.CropSquare();
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(500);
        height.Should().Be(500);
    }

    [Fact]
    public async Task Crop_AspectRatio_16_9_ProducesCorrectAspect()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 1000);

        // Act
        var result = await sourceStream.Crop(16.0 / 9.0);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        var aspectRatio = width / (double)height;
        aspectRatio.Should().BeApproximately(16.0 / 9.0, 0.01);
    }

    [Fact]
    public async Task Crop_AspectRatio_4_3_ProducesCorrectAspect()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1920, 1080);

        // Act
        var result = await sourceStream.Crop(4.0 / 3.0);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        var aspectRatio = width / (double)height;
        aspectRatio.Should().BeApproximately(4.0 / 3.0, 0.01);
    }

    [Fact]
    public async Task Crop_AspectRatio_WithAnchor_CropsFromSpecifiedPosition()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 1000);

        // Act
        var resultCenter = await sourceStream.Crop(2.0, CropFrom.Center);  // 2:1 landscape
        var (widthCenter, heightCenter) = await TestImageHelper.GetDimensions(resultCenter);

        // Assert
        widthCenter.Should().Be(1000);
        heightCenter.Should().Be(500);  // Cropped to 2:1
        var aspectRatio = widthCenter / (double)heightCenter;
        aspectRatio.Should().BeApproximately(2.0, 0.01);
    }

    // ======================================
    // PADDING TESTS
    // ======================================

    [Fact]
    public async Task Pad_ExactDimensions_ProducesCorrectSize()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(500, 500);

        // Act
        var result = await sourceStream.Pad(800, 600);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(800);
        height.Should().Be(600);
    }

    [Fact]
    public async Task Pad_Square_ProducesSquareImage()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(800, 400);

        // Act
        var result = await sourceStream.Pad(PadTo.Square);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(800);
        height.Should().Be(800);
        width.Should().Be(height);
    }

    [Fact]
    public async Task Pad_Landscape_16_9_ProducesCorrectAspect()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(800, 800);

        // Act
        var result = await sourceStream.Pad(PadTo.Landscape_16_9);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        var aspectRatio = width / (double)height;
        aspectRatio.Should().BeApproximately(16.0 / 9.0, 0.01);
    }

    // ======================================
    // ROTATION TESTS
    // ======================================

    [Fact]
    public async Task Rotate_90Degrees_SwapsDimensions()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.Rotate(90);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(500);  // Swapped
        height.Should().Be(1000);
    }

    [Fact]
    public async Task FlipHorizontal_MaintainsDimensions()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.FlipHorizontal();
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(1000);
        height.Should().Be(500);
    }

    [Fact]
    public async Task FlipVertical_MaintainsDimensions()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.FlipVertical();
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(1000);
        height.Should().Be(500);
    }

    // ======================================
    // FORMAT CONVERSION TESTS
    // ======================================

    [Fact]
    public async Task ConvertFormat_Webp_ConvertsSuccessfully()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(500, 500);

        // Act
        var result = await sourceStream.ConvertFormat("webp", 85);

        // Assert
        result.Should().NotBeNull();
        result.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ConvertFormat_Png_ConvertsSuccessfully()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(500, 500);

        // Act
        var result = await sourceStream.ConvertFormat("png");

        // Assert
        result.Should().NotBeNull();
        result.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ConvertFormat_UnsupportedFormat_ThrowsException()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(500, 500);

        // Act & Assert
        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await sourceStream.ConvertFormat("bmp")
        );
    }

    [Fact]
    public async Task OptimizeQuality_ReducesFileSize()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateGradientImage(1000, 1000);
        var originalSize = sourceStream.Length;

        // Act
        var result = await sourceStream.OptimizeQuality(50);

        // Assert
        result.Length.Should().BeLessThan(originalSize);
    }

    // ======================================
    // CHAINING TESTS
    // ======================================

    [Fact]
    public async Task Chaining_MultipleOperations_ProducesCorrectResult()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream
            .Resize(0.5, 0.5)           // 500×250
            .CropSquare()               // 250×250
            .Pad(400, 400);             // 400×400

        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(400);
        height.Should().Be(400);
    }

    [Fact]
    public async Task Chaining_ComplexPipeline_ExecutesSuccessfully()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(2000, 1000);

        // Act
        var result = await sourceStream
            .Crop(16.0 / 9.0)  // Simplified API!
            .ResizeX(1920)
            .OptimizeQuality(80);

        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(1920);
        var aspectRatio = width / (double)height;
        aspectRatio.Should().BeApproximately(16.0 / 9.0, 0.01);
    }

    // ======================================
    // BRANCHING TESTS
    // ======================================

    [Fact]
    public async Task Result_AllowsBranching()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        using var result = await sourceStream.Resize(0.5, 0.5).Result();

        var branch1 = await result.Branch().Resize(100, 100);
        var branch2 = await result.Branch().Resize(200, 200);

        var (width1, height1) = await TestImageHelper.GetDimensions(branch1);
        var (width2, height2) = await TestImageHelper.GetDimensions(branch2);

        // Assert
        width1.Should().Be(100);
        height1.Should().Be(100);
        width2.Should().Be(200);
        height2.Should().Be(200);
    }

    [Fact]
    public async Task Result_MultipleBranches_AllSucceed()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        using var result = await sourceStream.Result();

        using var small = await result.Branch().ResizeX(150);
        using var medium = await result.Branch().ResizeX(300);
        using var large = await result.Branch().ResizeX(600);

        var (w1, h1) = await TestImageHelper.GetDimensions(small);
        var (w2, h2) = await TestImageHelper.GetDimensions(medium);
        var (w3, h3) = await TestImageHelper.GetDimensions(large);

        // Assert
        w1.Should().Be(150);
        w2.Should().Be(300);
        w3.Should().Be(600);
        // Heights should all be proportional (1000:500 = 2:1)
        h1.Should().Be(75);
        h2.Should().Be(150);
        h3.Should().Be(300);
    }

    // ======================================
    // UTILITY METHOD TESTS
    // ======================================

    [Fact]
    public async Task ToBytes_ReturnsValidByteArray()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(100, 100);

        // Act
        var bytes = await sourceStream.ToBytes();

        // Assert
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ToBase64_ReturnsValidBase64String()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(100, 100);

        // Act
        var base64 = await sourceStream.ToBase64();

        // Assert
        base64.Should().NotBeNullOrEmpty();
        // Verify it's valid base64
        var bytes = Convert.FromBase64String(base64);
        bytes.Length.Should().BeGreaterThan(0);
    }

    // ======================================
    // EDGE CASE TESTS
    // ======================================

    [Fact]
    public async Task Resize_VerySmallImage_HandlesGracefully()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(10, 10);

        // Act
        var result = await sourceStream.Resize(5, 5);
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(5);
        height.Should().Be(5);
    }

    [Fact]
    public async Task Resize_VeryLargeScale_HandlesGracefully()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(100, 100);

        // Act
        var result = await sourceStream.Resize(5.0, 5.0);  // 500×500
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(500);
        height.Should().Be(500);
    }

    [Fact]
    public async Task CropSquare_TallImage_CreatesSquare()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(500, 1000);

        // Act
        var result = await sourceStream.CropSquare();
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(500);
        height.Should().Be(500);
    }

    [Fact]
    public async Task CropSquare_WideImage_CreatesSquare()
    {
        // Arrange
        using var sourceStream = TestImageHelper.CreateTestImage(1000, 500);

        // Act
        var result = await sourceStream.CropSquare();
        var (width, height) = await TestImageHelper.GetDimensions(result);

        // Assert
        width.Should().Be(500);
        height.Should().Be(500);
    }
}

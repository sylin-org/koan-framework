using FluentAssertions;
using Koan.Context.Models;
using Koan.Data.Vector.Connector.Weaviate.Partition;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Koan.Tests.Data.Vector.Weaviate.Specs.Partition;

/// <summary>
/// Tests for WeaviatePartitionMapper sanitization and naming
/// </summary>
public class WeaviatePartitionMapperSpec
{
    private readonly WeaviatePartitionMapper _mapper;
    private readonly Mock<ILogger<WeaviatePartitionMapper>> _loggerMock;

    public WeaviatePartitionMapperSpec()
    {
        _loggerMock = new Mock<ILogger<WeaviatePartitionMapper>>();
        _mapper = new WeaviatePartitionMapper(_loggerMock.Object);
    }

    #region Sanitization Tests

    [Theory]
    [InlineData("project-123", "project_123")]
    [InlineData("PROJECT_ABC", "project_abc")]
    [InlineData("my-cool-project!", "my_cool_project")]
    [InlineData("test@#$%project", "test_project")]
    [InlineData("___leading___", "leading")]
    [InlineData("trailing___", "trailing")]
    [InlineData("multiple___underscores", "multiple_underscores")]
    public void SanitizePartitionId_InvalidCharacters_SanitizesCorrectly(string input, string expected)
    {
        // Act
        var result = _mapper.SanitizePartitionId(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SanitizePartitionId_EmptyInput_ReturnsDefault(string? input)
    {
        // Act
        var result = _mapper.SanitizePartitionId(input!);

        // Assert
        result.Should().Be("default");
    }

    [Fact]
    public void SanitizePartitionId_OnlySpecialCharacters_ReturnsDefault()
    {
        // Act
        var result = _mapper.SanitizePartitionId("!@#$%^&*()");

        // Assert
        result.Should().Be("default");
    }

    #endregion

    #region Storage Name Mapping Tests

    [Fact]
    public void MapStorageName_ValidPartition_ReturnsCorrectClassName()
    {
        // Act
        var result = _mapper.MapStorageName<DocumentChunk>("koan-framework");

        // Assert
        result.Should().Be("KoanDocumentChunk_koan_framework");
    }

    [Fact]
    public void MapStorageName_ComplexPartitionId_SanitizesAndMaps()
    {
        // Act
        var result = _mapper.MapStorageName<DocumentChunk>("My-Project#123!");

        // Assert
        result.Should().Be("KoanDocumentChunk_my_project_123");
    }

    [Fact]
    public void MapStorageName_VeryLongPartitionId_Truncates()
    {
        // Arrange
        var longId = new string('a', 300);

        // Act
        var result = _mapper.MapStorageName<DocumentChunk>(longId);

        // Assert
        result.Length.Should().BeLessOrEqualTo(256);
        result.Should().StartWith("KoanDocumentChunk_");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MapStorageName_EmptyPartitionId_ThrowsArgumentException(string emptyId)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _mapper.MapStorageName<DocumentChunk>(emptyId));
    }

    #endregion

    #region Base Name Tests

    [Fact]
    public void GetBaseName_DocumentChunk_ReturnsKoanPrefixed()
    {
        // Act
        var result = _mapper.GetBaseName<DocumentChunk>();

        // Assert
        result.Should().Be("KoanDocumentChunk");
    }

    [Fact]
    public void GetBaseName_GenericType_RemovesGenericMarker()
    {
        // Act
        var result = _mapper.GetBaseName<List<string>>();

        // Assert
        result.Should().NotContain("`");
        result.Should().StartWith("Koan");
    }

    #endregion

    #region Isolation Tests

    [Fact]
    public void MapStorageName_DifferentPartitions_ProduceDifferentClassNames()
    {
        // Act
        var class1 = _mapper.MapStorageName<DocumentChunk>("project-a");
        var class2 = _mapper.MapStorageName<DocumentChunk>("project-b");

        // Assert
        class1.Should().NotBe(class2);
        class1.Should().Be("KoanDocumentChunk_project_a");
        class2.Should().Be("KoanDocumentChunk_project_b");
    }

    [Fact]
    public void MapStorageName_SamePartitionMultipleCalls_ReturnsConsistentName()
    {
        // Act
        var class1 = _mapper.MapStorageName<DocumentChunk>("project-123");
        var class2 = _mapper.MapStorageName<DocumentChunk>("project-123");

        // Assert
        class1.Should().Be(class2);
    }

    #endregion
}

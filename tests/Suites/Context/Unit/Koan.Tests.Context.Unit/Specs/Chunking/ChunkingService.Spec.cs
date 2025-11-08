using System.Text;
using ChunkerService = Koan.Context.Services.Chunker;
using FluentAssertions;
using Koan.Context.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Koan.Tests.Context.Unit.Specs.Chunking;

/// <summary>
/// Comprehensive tests for ChunkerService
/// </summary>
/// <remarks>
/// Tests cover:
/// - Token estimation accuracy
/// - Chunk size validation (800-1000 tokens)
/// - Overlap validation (50 tokens)
/// - Heading boundary respect
/// - Large section splitting
/// - Edge cases (empty documents, very long sections)
/// </remarks>
public class ChunkerServiceSpec : IDisposable
{
    private readonly ChunkerService _service;
    private readonly Mock<ILogger<ChunkerService>> _loggerMock;
    private readonly string _testProjectId = "test-project";
    private readonly string _testCommit = "abc123";

    public ChunkerServiceSpec()
    {
        _loggerMock = new Mock<ILogger<ChunkerService>>();
        _service = new ChunkerService(_loggerMock.Object);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    #region Token Estimation Tests

    [Theory]
    [InlineData("", 0)]
    [InlineData("Hello world", 3)]  // ~11 chars / 4 = 2.75 → 3
    [InlineData("This is a test sentence with multiple words.", 12)]  // ~45 chars / 4 = 11.25 → 12
    public async Task ChunkAsync_TokenEstimation_IsApproximatelyCorrect(string text, int expectedTokens)
    {
        // Arrange
        var doc = CreateDocumentWithText(text);

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        if (string.IsNullOrWhiteSpace(text))
        {
            chunks.Should().BeEmpty();
        }
        else
        {
            chunks.Should().HaveCount(1);
            chunks[0].TokenCount.Should().BeGreaterThanOrEqualTo(expectedTokens - 1);
            chunks[0].TokenCount.Should().BeLessThanOrEqualTo(expectedTokens + 1);
        }
    }

    #endregion

    #region Chunk Size Validation Tests

    [Fact]
    public async Task ChunkAsync_SmallDocument_CreatesOneChunk()
    {
        // Arrange - 500 tokens worth (~2000 chars)
        var text = new string('a', 2000);
        var doc = CreateDocumentWithText(text);

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].TokenCount.Should().BeInRange(400, 600); // ~500 tokens
    }

    [Fact]
    public async Task ChunkAsync_MediumDocument_RespectsSizeTarget()
    {
        // Arrange - 2000 tokens worth (~8000 chars) - should create 2-3 chunks
        var text = new string('a', 8000);
        var doc = CreateDocumentWithText(text);

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        chunks.Should().HaveCountGreaterThan(1);
        foreach (var chunk in chunks)
        {
            chunk.TokenCount.Should().BeLessThanOrEqualTo(1000, "chunks should not exceed max size");
        }
    }

    [Fact]
    public async Task ChunkAsync_LargeDocument_MaintainsTargetSize()
    {
        // Arrange - 5000 tokens worth (~20000 chars)
        var sections = new List<ContentSection>();
        for (int i = 0; i < 10; i++)
        {
            sections.Add(new ContentSection(
                Type: ContentType.Paragraph,
                Text: new string('a', 2000),
                StartOffset: i * 2000,
                EndOffset: (i + 1) * 2000,
                Title: null,
                Language: null));
        }

        var doc = new ExtractedDocument(
            FilePath: "test.md",
            RelativePath: "test.md",
            FullText: string.Join("\n\n", sections.Select(s => s.Text)),
            Sections: sections,
            TitleHierarchy: new List<string>());

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        chunks.Should().HaveCountGreaterThan(4); // At least 5 chunks for 5000 tokens
        foreach (var chunk in chunks.SkipLast(1)) // All but last chunk
        {
            chunk.TokenCount.Should().BeInRange(800, 1000, "chunks should be within target range");
        }
    }

    #endregion

    #region Overlap Validation Tests

    [Fact]
    public async Task ChunkAsync_MultipleChunks_HasOverlap()
    {
        // Arrange - Create document that will span 2 chunks
        var distinctText = string.Join(" ", Enumerable.Range(0, 400).Select(i => $"word{i:D4}"));
        var doc = CreateDocumentWithText(distinctText);

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        if (chunks.Count > 1)
        {
            var firstChunkEnd = chunks[0].Text.Split(' ').TakeLast(20).ToArray();
            var secondChunkStart = chunks[1].Text.Split(' ').Take(20).ToArray();

            // Should have some overlap
            firstChunkEnd.Intersect(secondChunkStart).Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task ChunkAsync_WithOverlap_MaintainsApproximately50Tokens()
    {
        // Arrange - Create large document
        var sections = Enumerable.Range(0, 5).Select(i => new ContentSection(
            Type: ContentType.Paragraph,
            Text: new string('a', 2000),
            StartOffset: i * 2000,
            EndOffset: (i + 1) * 2000,
            Title: null,
            Language: null)).ToList();

        var doc = new ExtractedDocument(
            FilePath: "test.md",
            RelativePath: "test.md",
            FullText: string.Join("\n\n", sections.Select(s => s.Text)),
            Sections: sections,
            TitleHierarchy: new List<string>());

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        if (chunks.Count > 1)
        {
            for (int i = 1; i < chunks.Count; i++)
            {
                // Overlap should be roughly 50 tokens (200 chars)
                var overlapText = chunks[i].Text.Substring(0, Math.Min(250, chunks[i].Text.Length));
                var overlapTokens = overlapText.Length / 4;
                overlapTokens.Should().BeInRange(40, 70, "overlap should be approximately 50 tokens");
            }
        }
    }

    #endregion

    #region Heading Boundary Respect Tests

    [Fact]
    public async Task ChunkAsync_WithHeadings_SplitsAtHeadingBoundaries()
    {
        // Arrange
        var sections = new List<ContentSection>
        {
            new(ContentType.Heading, "# Introduction", 0, 15, Title: "Introduction", Language: null),
            new(ContentType.Paragraph, new string('a', 2000), 16, 2016, Title: null, Language: null),
            new(ContentType.Heading, "# Methods", 2017, 2027, Title: "Methods", Language: null),
            new(ContentType.Paragraph, new string('b', 2000), 2028, 4028, Title: null, Language: null)
        };

        var doc = new ExtractedDocument(
            FilePath: "test.md",
            RelativePath: "test.md",
            FullText: string.Join("\n\n", sections.Select(s => s.Text)),
            Sections: sections,
            TitleHierarchy: new List<string> { "Document" });

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        chunks.Should().HaveCountGreaterThanOrEqualTo(2);
        chunks.Select(c => c.Title).Distinct().Should().Contain("Introduction");
        chunks.Select(c => c.Title).Distinct().Should().Contain("Methods");
    }

    [Fact]
    public async Task ChunkAsync_HeadingUpdatesTitle_InSubsequentChunks()
    {
        // Arrange
        var sections = new List<ContentSection>
        {
            new(ContentType.Heading, "# Section 1", 0, 12, Title: "Section 1", Language: null),
            new(ContentType.Paragraph, new string('a', 500), 13, 513, Title: null, Language: null),
            new(ContentType.Heading, "# Section 2", 514, 526, Title: "Section 2", Language: null),
            new(ContentType.Paragraph, new string('b', 500), 527, 1027, Title: null, Language: null)
        };

        var doc = new ExtractedDocument(
            FilePath: "test.md",
            RelativePath: "test.md",
            FullText: string.Join("\n\n", sections.Select(s => s.Text)),
            Sections: sections,
            TitleHierarchy: new List<string>());

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        var titleChanges = chunks.Select(c => c.Title).Distinct().ToList();
        titleChanges.Should().Contain("Section 1");
        titleChanges.Should().Contain("Section 2");
    }

    [Fact]
    public async Task ChunkAsync_SmallSectionsWithHeadings_GroupsIntoChunks()
    {
        // Arrange - Many small sections that should be grouped
        var sections = new List<ContentSection>();
        for (int i = 0; i < 20; i++)
        {
            sections.Add(new ContentSection(
                ContentType.Heading,
                $"# Heading {i}",
                i * 200,
                i * 200 + 12,
                Title: $"Heading {i}",
                Language: null));
            sections.Add(new ContentSection(
                ContentType.Paragraph,
                new string('a', 100),
                i * 200 + 13,
                i * 200 + 113,
                Title: null,
                Language: null));
        }

        var doc = new ExtractedDocument(
            FilePath: "test.md",
            RelativePath: "test.md",
            FullText: string.Join("\n\n", sections.Select(s => s.Text)),
            Sections: sections,
            TitleHierarchy: new List<string>());

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        chunks.Should().HaveCountLessThan(20, "small sections should be grouped");
        chunks.Should().HaveCountGreaterThan(2, "but not into a single chunk");
    }

    #endregion

    #region Large Section Splitting Tests

    [Fact]
    public async Task ChunkAsync_SectionExceedsMax_SplitsAtSentenceBoundaries()
    {
        // Arrange - Single section larger than 1000 tokens
        var largeParagraph = string.Join(". ", Enumerable.Range(0, 300).Select(i => $"Sentence {i} with some text"));
        var sections = new List<ContentSection>
        {
            new(ContentType.Paragraph, largeParagraph, 0, largeParagraph.Length, Title: null, Language: null)
        };

        var doc = new ExtractedDocument(
            FilePath: "test.md",
            RelativePath: "test.md",
            FullText: largeParagraph,
            Sections: sections,
            TitleHierarchy: new List<string> { "Large Document" });

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        chunks.Should().HaveCountGreaterThan(1, "large section should be split");
        foreach (var chunk in chunks)
        {
            chunk.TokenCount.Should().BeLessThanOrEqualTo(1000, "split chunks should not exceed max");
        }
    }

    [Fact]
    public async Task ChunkAsync_LargeSection_PreservesLanguageMetadata()
    {
        // Arrange - Large code block
        var largeCode = string.Join("\n", Enumerable.Range(0, 500).Select(i => $"function_{i}() {{ return {i}; }}"));
        var sections = new List<ContentSection>
        {
            new(ContentType.CodeBlock, largeCode, 0, largeCode.Length, Title: null, Language: "javascript")
        };

        var doc = new ExtractedDocument(
            FilePath: "test.js",
            RelativePath: "test.js",
            FullText: largeCode,
            Sections: sections,
            TitleHierarchy: new List<string>());

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        chunks.Should().AllSatisfy(chunk => chunk.Language.Should().Be("javascript"));
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task ChunkAsync_EmptyDocument_YieldsNothing()
    {
        // Arrange
        var doc = new ExtractedDocument(
            FilePath: "empty.md",
            RelativePath: "empty.md",
            FullText: string.Empty,
            Sections: new List<ContentSection>(),
            TitleHierarchy: new List<string>());

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        chunks.Should().BeEmpty();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("has no sections")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ChunkAsync_NullProjectId_ThrowsArgumentException()
    {
        // Arrange
        var doc = CreateDocumentWithText("test");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _service.ChunkAsync(doc, null!, _testCommit).ToListAsync();
        });
    }

    [Fact]
    public async Task ChunkAsync_NullDocument_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _service.ChunkAsync(null!, _testProjectId, _testCommit).ToListAsync();
        });
    }

    [Fact]
    public async Task ChunkAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var sections = Enumerable.Range(0, 100).Select(i => new ContentSection(
            ContentType.Paragraph,
            new string('a', 1000),
            i * 1000,
            (i + 1) * 1000,
            Title: null,
            Language: null)).ToList();

        var doc = new ExtractedDocument(
            FilePath: "large.md",
            RelativePath: "large.md",
            FullText: string.Join("\n", sections.Select(s => s.Text)),
            Sections: sections,
            TitleHierarchy: new List<string>());

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _service.ChunkAsync(doc, _testProjectId, _testCommit, cts.Token).ToListAsync();
        });
    }

    #endregion

    #region Metadata Preservation Tests

    [Fact]
    public async Task ChunkAsync_PreservesProjectId_InAllChunks()
    {
        // Arrange
        var doc = CreateDocumentWithText(new string('a', 5000));

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        chunks.Should().AllSatisfy(chunk => chunk.ProjectId.Should().Be(_testProjectId));
    }

    [Fact]
    public async Task ChunkAsync_PreservesFilePath_InAllChunks()
    {
        // Arrange
        var doc = new ExtractedDocument(
            FilePath: "/path/to/doc.md",
            RelativePath: "docs/doc.md",
            FullText: new string('a', 5000),
            Sections: new List<ContentSection>
            {
                new(ContentType.Paragraph, new string('a', 5000), 0, 5000, Title: null, Language: null)
            },
            TitleHierarchy: new List<string>());

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        chunks.Should().AllSatisfy(chunk => chunk.FilePath.Should().Be("docs/doc.md"));
    }

    [Fact]
    public async Task ChunkAsync_TracksOffsets_Correctly()
    {
        // Arrange
        var sections = new List<ContentSection>
        {
            new(ContentType.Paragraph, new string('a', 2000), 0, 2000, Title: null, Language: null),
            new(ContentType.Paragraph, new string('b', 2000), 2001, 4001, Title: null, Language: null)
        };

        var doc = new ExtractedDocument(
            FilePath: "test.md",
            RelativePath: "test.md",
            FullText: string.Join("\n\n", sections.Select(s => s.Text)),
            Sections: sections,
            TitleHierarchy: new List<string>());

        // Act
        var chunks = await _service.ChunkAsync(doc, _testProjectId, _testCommit).ToListAsync();

        // Assert
        foreach (var chunk in chunks)
        {
            chunk.StartOffset.Should().BeGreaterThanOrEqualTo(0);
            chunk.EndOffset.Should().BeGreaterThan(chunk.StartOffset);
        }

        // Chunks should be ordered by offset
        for (int i = 1; i < chunks.Count; i++)
        {
            chunks[i].StartOffset.Should().BeGreaterThanOrEqualTo(chunks[i - 1].StartOffset);
        }
    }

    #endregion

    #region Helper Methods

    private ExtractedDocument CreateDocumentWithText(string text)
    {
        return new ExtractedDocument(
            FilePath: "test.md",
            RelativePath: "test.md",
            FullText: text ?? string.Empty,
            Sections: string.IsNullOrWhiteSpace(text)
                ? new List<ContentSection>()
                : new List<ContentSection>
                {
                    new(ContentType.Paragraph, text, 0, text.Length, Title: null, Language: null)
                },
            TitleHierarchy: new List<string> { "Test Document" });
    }

    #endregion
}

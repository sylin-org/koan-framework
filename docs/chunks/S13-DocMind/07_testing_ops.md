## **Testing & Operations Plan**

### 1. Test Strategy Overview

- **Unit Tests**: Cover domain models, value objects, and service logic (e.g., prompt builders, suggestion ranking).
- **Integration Tests**: Exercise upload → completion pipeline using in-memory storage and Koan AI test doubles.
- **End-to-End Smoke**: Run against docker-compose stack with Ollama + Weaviate to validate real integrations.
- **Regression Automation**: GitHub Actions workflow running unit + integration tests on every PR; optional nightly E2E job.

### 2. Suggested Test Projects

| Project | Purpose | Key Scenarios |
|---------|---------|---------------|
| `S13.DocMind.Tests.Unit` | Validate domain + service logic | `DocumentProcessingSummaryTests`, `TemplateSuggestionServiceTests`, `InsightAggregationServiceTests` |
| `S13.DocMind.Tests.Integration` | Pipeline + API flows | `UploadDocument_ProcessesToCompletion`, `AssignProfile_TriggersRequeue`, `Timeline_ReturnsOrderedEvents` |
| `S13.DocMind.Tests.E2E` (optional) | Runs against compose stack | `UploadPdf_GeneratesVisionAndTextInsights`, `ReplayDocument_ReprocessesSuccessfully` |

### 3. Integration Test Blueprint

```csharp
public class UploadDocument_ProcessesToCompletion : IClassFixture<DocMindApiFactory>
{
    private readonly HttpClient _client;
    private readonly FakeAiProvider _ai;

    public UploadDocument_ProcessesToCompletion(DocMindApiFactory factory)
    {
        _client = factory.CreateClient();
        _ai = factory.AiProvider;
    }

    [Fact]
    public async Task EndToEnd()
    {
        using var form = new MultipartFormDataContent
        {
            { new StreamContent(TestDocuments.SamplePdf()), "file", "sample.pdf" }
        };

        var upload = await _client.PostAsync("/api/documents/upload", form);
        upload.EnsureSuccessStatusCode();
        var receipt = await upload.Content.ReadFromJsonAsync<DocumentUploadReceipt>();

        await TestHelpers.WaitForStatusAsync(_client, receipt.DocumentId, DocumentProcessingStatus.Completed, TimeSpan.FromSeconds(45));

        var timeline = await _client.GetFromJsonAsync<IReadOnlyList<TimelineEntryResponse>>($"/api/documents/{receipt.DocumentId}/timeline");
        timeline.Should().Contain(e => e.Stage == ProcessingStage.ExtractText);
        timeline.Should().Contain(e => e.Stage == ProcessingStage.GenerateInsights);

        var insights = await _client.GetFromJsonAsync<IReadOnlyList<DocumentInsightResponse>>($"/api/documents/{receipt.DocumentId}/insights");
        insights.Should().NotBeEmpty();
    }
}
```

### 4. Test Data & Fixtures
- Provide `tests/fixtures` directory with sample PDF, DOCX, PNG, and corrupted file.
- Implement `FakeAiProvider` returning deterministic responses for predictable assertions.
- Use `MongoDbFixture` to spin up ephemeral Mongo instance for integration tests (or connect to compose Mongo when running E2E).

### 5. CI Pipeline Outline
1. `dotnet format` (optional) – ensure consistent style.
2. `dotnet test` – run unit + integration tests (with fake providers).
3. `npm test -- --runTestsByPath` (if Angular tests present) – validate UI utilities.
4. Generate OpenAPI spec + MCP manifest; compare against committed baseline to catch breaking changes.
5. Archive test artifacts (logs, coverage, sample outputs).

### 6. Operational Playbooks
- **Daily reset**: Run `scripts/docmind-reset.sh` to clear Mongo collections, storage folder, and Weaviate schema.
- **Model install**: `scripts/install-models.sh` ensures Ollama models required by the sample are present before demos.
- **Replay**: `dotnet run --project samples/S13.DocMind -- replay --document <id>` to requeue documents for troubleshooting.
- **Health checks**: Monitor `/health`, `/health/storage`, `/health/embedding`, `/health/models` endpoints exposed by the API.

### 7. Observability Dashboards
- Leverage Koan boot report output to confirm provider readiness.
- Configure OpenTelemetry exporters to send traces/metrics to the provided `otel-collector` compose override.
- Build Grafana dashboard showing queue depth, processing durations, and model latency using metrics emitted by `DocumentAnalysisPipeline`.

### 8. Release Readiness Checklist
- ✅ All tests passing in CI and compose environment.
- ✅ OpenAPI + MCP manifests regenerated and reviewed.
- ✅ Documentation updated (README, chunk docs, Postman collection).
- ✅ Demo script validated (upload, assignment, insight review, template generation, MCP automation).
- ✅ Rollback plan defined (restore previous compose stack, disable new controllers via feature flag if needed).

This testing and operations plan ensures the refactored sample remains reliable, observable, and easy to demo while embracing Koan integration best practices.

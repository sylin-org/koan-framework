
#### **Integration Test Requirements**
```csharp
namespace S13.DocMind.Tests.Integration
{
    [Collection("DatabaseCollection")]
    public class DocumentProcessingWorkflowTests : IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        [Fact]
        public async Task UploadDocument_ShouldTriggerCompleteProcessingWorkflow()
        {
            // Arrange
            var testDocument = CreateTestPdfDocument();
            var uploadRequest = new MultipartFormDataContent();
            uploadRequest.Add(new ByteArrayContent(testDocument.Content), "files", testDocument.FileName);

            // Act - Upload document
            var uploadResponse = await _client.PostAsync("/api/documents/upload", uploadRequest);

            // Assert - Document uploaded successfully
            uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var document = await ParseResponse<Document>(uploadResponse);
            document.FileName.Should().Be(testDocument.FileName);

            // Wait for background processing
            await WaitForProcessingComplete(document.Id, TimeSpan.FromSeconds(30));

            // Assert - Processing completed
            var processingHistory = await GetProcessingHistory(document.Id);
            processingHistory.Should().ContainSingle(e => e.Stage == ProcessingStage.ProcessingCompleted);

            // Assert - Analysis generated
            var analysis = await _client.GetFromJsonAsync<DocumentAnalysis>($"/api/documents/{document.Id}/analysis");
            analysis.Should().NotBeNull();
            analysis.ConfidenceScore.Should().BeGreaterThan(0.5);

            // Assert - Template matching occurred
            var similarTemplates = await _client.GetFromJsonAsync<List<DocumentTemplate>>($"/api/documents/{document.Id}/similar-templates");
            similarTemplates.Should().NotBeNull();
        }

        [Fact]
        public async Task ProcessDocument_WithMediumFile_ShouldHandleGracefully()
        {
            // Test with 8MB file (aligned with sample guidance)
            var largeDocument = CreateLargeTestDocument(8 * 1024 * 1024);
            // ... test implementation
        }

        [Fact]
        public async Task AIAnalysis_WithInvalidContent_ShouldReturnErrorGracefully()
        {
            // Test error handling for corrupted or invalid content
            // ... test implementation
        }

        private async Task WaitForProcessingComplete(Guid documentId, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeout)
            {
                var document = await _client.GetFromJsonAsync<Document>($"/api/documents/{documentId}");
                if (document?.State == ProcessingState.Completed || document?.State == ProcessingState.Failed)
                {
                    return;
                }

                await Task.Delay(1000);
            }

            throw new TimeoutException($"Document processing did not complete within {timeout}");
        }
    }

    // Load testing specification
    [Fact]
    public async Task LoadTest_ConcurrentDocumentProcessing_Optional()
    {
        const int concurrentDocuments = 6; // stretch scenario for workshops
        const int maxProcessingTimeMinutes = 3;

        var tasks = Enumerable.Range(0, concurrentDocuments)
            .Select(i => ProcessTestDocument($"test-doc-{i}.txt"))
            .ToArray();

        var completedTasks = await Task.WhenAll(tasks);

        // Assert all documents processed successfully
        completedTasks.Should().AllSatisfy(result =>
            result.State.Should().Be(ProcessingState.Completed));

        // Assert reasonable processing times
        var avgProcessingTime = completedTasks.Average(r => r.ProcessingDuration.TotalSeconds);
        avgProcessingTime.Should().BeLessThan(maxProcessingTimeMinutes * 60);
    }
}
```

**Recommended test progression:**

1. **Smoke walkthrough** – run `DocumentProcessingWorkflowTests.UploadDocument_ShouldTriggerCompleteProcessingWorkflow` with the default sample PDF.
2. **Medium file resilience** – execute `ProcessDocument_WithMediumFile_ShouldHandleGracefully` using the bundled 8 MB fixture.
3. **Optional stretch** – enable the `[Category("Load")]` collection to run `LoadTest_ConcurrentDocumentProcessing_Optional` once infrastructure resources are scaled.

### **7. Deployment & Operations Specifications**

#### **Docker Compose Development Setup (Following S5/S8 Patterns)**

Based on successful patterns from S5.Recs and S8.Flow/S8.Location samples, S13.DocMind provides multiple deployment scenarios:

##### **Option 1: API with Embedded Client (S5.Recs Pattern)**
```yaml
# docker-compose.yml - Simple embedded client in API wwwroot
version: '3.8'
services:
  mongodb:
    image: mongo:7
    container_name: s13-docmind-mongo
    healthcheck:
      test: ["CMD", "mongosh", "--eval", "db.adminCommand('ping')"]
      interval: 5s
      timeout: 5s
      retries: 10
    ports:
      - "4920:27017"
    volumes:
      - mongo_data:/data/db
    environment:
      MONGO_INITDB_DATABASE: s13docmind

  # Optional: Weaviate for vector embeddings (can be disabled)
  weaviate:
    image: semitechnologies/weaviate:1.22.4
    container_name: s13-docmind-weaviate
    ports:
      - "4922:8080"
    environment:
      QUERY_DEFAULTS_LIMIT: 25
      AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED: 'true'
      PERSISTENCE_DATA_PATH: '/var/lib/weaviate'
      DEFAULT_VECTORIZER_MODULE: 'none'
      ENABLE_MODULES: 'backup-filesystem'
      CLUSTER_HOSTNAME: 'node1'
    volumes:
      - weaviate_data:/var/lib/weaviate
    healthcheck:
      test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:8080/v1/.well-known/ready"]
      interval: 5s
      timeout: 5s
      retries: 10

  # Ollama for local AI processing
  ollama:
    image: ollama/ollama:latest
    container_name: s13-docmind-ollama
    ports:
      - "4924:11434"
    volumes:
      - ollama_models:/root/.ollama
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:11434/api/version"]
      interval: 30s
      timeout: 10s
      retries: 5
    environment:
      - OLLAMA_MODELS_DIR=/root/.ollama

  # Main API with embedded web client in wwwroot (S5.Recs pattern)
  docmind-api:
    build:
      context: ../../..  # Build from repo root like S8 samples
      dockerfile: samples/S13.DocMind/Dockerfile
    container_name: s13-docmind-api
    environment:
      ASPNETCORE_URLS: http://+:4925
      ASPNETCORE_ENVIRONMENT: Development
      # Simplified Koan provider configuration (MongoDB + optional Weaviate)
      Koan__Data__Providers__mongodb__connectionString: mongodb://mongodb:27017
      Koan__Data__Providers__mongodb__database: s13docmind
      Koan__Data__Providers__weaviate__endpoint: http://weaviate:8080
      # AI Configuration
      Koan__AI__Ollama__BaseUrl: http://ollama:11434
      Koan__AI__OpenAI__ApiKey: ${OPENAI_API_KEY:-}
      # Document processing limits
      S13__DocMind__MaxDocumentSizeMB: 50
      S13__DocMind__ConcurrentProcessingLimit: 10
    depends_on:
      mongodb:
        condition: service_healthy
      weaviate:
        condition: service_healthy
      ollama:
        condition: service_healthy
    ports:
      - "4925:4925"
    volumes:
      - document_storage:/app/storage  # For large document files
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:4925/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s

volumes:
  mongo_data:
  weaviate_data:
  ollama_models:
  document_storage:
```

##### **Option 2: Separate Client Container (S8.Location Pattern)**
```yaml
# docker-compose.separate-client.yml - Client as separate nginx container
version: '3.8'
services:
  # ... same infrastructure services as above ...

  # API without embedded client
  docmind-api:
    build:
      context: ../../..
      dockerfile: samples/S13.DocMind/S13.DocMind.Api/Dockerfile
    container_name: s13-docmind-api
    environment:
      ASPNETCORE_URLS: http://+:4926
      # ... same environment variables as Option 1 ...
    depends_on:
      mongodb:
        condition: service_healthy
      weaviate:
        condition: service_healthy
      ollama:
        condition: service_healthy
    ports:
      - "4926:4926"

  # Separate React/Vue client container (S8.Location pattern)
  docmind-client:
    build:
      context: ../S13.DocMind.Client
      dockerfile: Dockerfile
    container_name: s13-docmind-client
    ports:
      - "4927:80"  # Client on port 4927
    depends_on:
      - docmind-api
    healthcheck:
      test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:80/"]
      interval: 30s
      timeout: 10s
      retries: 3
    # nginx configuration for API proxying built into Dockerfile
```

#### **Dockerfile Configurations**

##### **API Dockerfile (Based on S8 Pattern)**
```dockerfile
# samples/S13.DocMind/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .

# Restore and build from repo root context (S8 pattern)
RUN dotnet restore samples/S13.DocMind/S13.DocMind.csproj
RUN dotnet publish samples/S13.DocMind/S13.DocMind.csproj -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app/publish .

# Create storage directory for document files
RUN mkdir -p /app/storage && chmod 755 /app/storage

# Expose port
EXPOSE 4925

ENTRYPOINT ["dotnet", "S13.DocMind.dll"]
```

##### **Separate Client Dockerfile (S8.Location Pattern)**
```dockerfile
# samples/S13.DocMind.Client/Dockerfile
FROM node:20-alpine AS build
WORKDIR /app

# Copy package files
COPY package*.json ./
RUN npm ci

# Copy source and build
COPY . .
RUN npm run build

# Production stage with nginx
FROM nginx:alpine

# Install wget for health checks
RUN apk add --no-cache wget

# Copy built client files
COPY --from=build /app/dist /usr/share/nginx/html

# Create nginx configuration with API proxy
RUN echo 'server { \
    listen 80; \
    server_name localhost; \
    root /usr/share/nginx/html; \
    index index.html; \
    \
    # Client-side routing \
    location / { \
        try_files $uri $uri/ /index.html; \
    } \
    \
    # API proxy to backend \
    location /api/ { \
        proxy_pass http://docmind-api:4926/api/; \
        proxy_http_version 1.1; \
        proxy_set_header Upgrade $http_upgrade; \
        proxy_set_header Connection "upgrade"; \
        proxy_set_header Host $host; \
        proxy_set_header X-Real-IP $remote_addr; \
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for; \
        proxy_set_header X-Forwarded-Proto $scheme; \
        proxy_read_timeout 300s; \
        proxy_connect_timeout 75s; \
        client_max_body_size 50m; \
    } \
    \
    # Health check endpoint \
    location /health { \
        proxy_pass http://docmind-api:4926/health; \
        proxy_http_version 1.1; \
        proxy_set_header Host $host; \
    } \
    \
    # WebSocket support for real-time updates \
    location /ws { \
        proxy_pass http://docmind-api:4926/ws; \
        proxy_http_version 1.1; \
        proxy_set_header Upgrade $http_upgrade; \
        proxy_set_header Connection "upgrade"; \
        proxy_set_header Host $host; \
    } \
}' > /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

#### **API Static File Configuration (S5.Recs Pattern)**

For embedded client hosting in API wwwroot, the Koan framework auto-wires static files:

##### **Program.cs Configuration**
```csharp
using S13.DocMind;

var builder = WebApplication.CreateBuilder(args);

// Single line enables Koan with auto-static file serving (S5 pattern)
builder.Services.AddKoan()
    .AsWebApi()           // Enables API controllers
    .AsProxiedApi()       // Enables reverse proxy support
    .WithRateLimit();     // Adds rate limiting

var app = builder.Build();

// Koan.Web startup filter auto-wires:
// - Static files from wwwroot
// - Controller routing
// - Swagger endpoints
// - Health checks

// Custom middleware for document processing
app.UseDocumentProcessing();  // Custom middleware for file uploads

app.Run();

namespace S13.DocMind
{
    public partial class Program { }
}
```

##### **Client Files in wwwroot Structure**
```
samples/S13.DocMind/wwwroot/
├── index.html              # Main SPA entry point
├── js/
│   ├── app.js             # Main application logic
│   ├── document-upload.js # Document upload handling
│   ├── template-editor.js # Template editing
│   └── analysis-viewer.js # Analysis result viewer
├── css/
│   ├── styles.css         # Main stylesheet
│   └── components.css     # Component styles
├── images/
│   ├── logo.png
│   └── icons/
└── lib/                   # Third-party libraries
    ├── axios.min.js
    ├── marked.min.js      # Markdown rendering
    └── highlight.min.js   # Code syntax highlighting
```

##### **Client-Side API Integration**
```javascript
// wwwroot/js/app.js - API integration with auto-discovery
class DocMindApi {
    constructor() {
        // Auto-detect API base URL (works in both embedded and proxied scenarios)
        this.baseUrl = window.location.origin;
        this.apiPath = '/api';
    }

    async uploadDocuments(files) {
        const formData = new FormData();
        files.forEach(file => formData.append('files', file));

        const response = await fetch(`${this.baseUrl}${this.apiPath}/documents/upload`, {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            throw new Error(`Upload failed: ${response.statusText}`);
        }

        return response.json();
    }

    async getDocumentAnalysis(documentId) {
        const response = await fetch(`${this.baseUrl}${this.apiPath}/documents/${documentId}/analysis`);
        return response.json();
    }

    async generateTemplate(prompt) {
        const response = await fetch(`${this.baseUrl}${this.apiPath}/templates/generate`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ prompt })
        });
        return response.json();
    }

    // WebSocket for real-time processing updates
    connectToProcessingUpdates(documentId, callback) {
        const ws = new WebSocket(`ws://${window.location.host}/ws/documents/${documentId}/processing`);
        ws.onmessage = (event) => callback(JSON.parse(event.data));
        return ws;
    }
}
```

#### **Environment-Specific Configurations**

##### **Development (docker-compose.yml)**
```yaml
# Optimized for development with local Ollama
environment:
  ASPNETCORE_ENVIRONMENT: Development
  # Use local Ollama instance
  Koan__AI__Ollama__BaseUrl: http://host.docker.internal:11434
  # Enable detailed logging
  Logging__LogLevel__S13.DocMind: Debug
  Logging__LogLevel__Koan.AI: Debug
  # Relaxed file upload limits for testing
  S13__DocMind__MaxDocumentSizeMB: 100
  S13__DocMind__AllowTestDocuments: "true"
```

##### **Production (docker-compose.production.yml)**
```yaml
# Production with external AI services
environment:
  ASPNETCORE_ENVIRONMENT: Production
  # Use OpenAI for production AI
  Koan__AI__OpenAI__ApiKey: ${OPENAI_API_KEY}
  Koan__AI__OpenAI__Model: gpt-4-turbo
  # Strict security settings
  S13__DocMind__MaxDocumentSizeMB: 50
  S13__DocMind__AllowTestDocuments: "false"
  S13__DocMind__RequireAuthentication: "true"
  # Production database with replication
  Koan__Data__Providers__mongodb__connectionString: mongodb://mongo-primary:27017,mongo-secondary:27017/s13docmind?replicaSet=rs0
```

#### **Quick Start Scripts (S8 Pattern)**

##### **start.sh - Development Startup**
```bash
#!/bin/bash
# samples/S13.DocMind/start.sh

echo "🚀 Starting S13.DocMind Development Environment..."

# Check prerequisites
command -v docker >/dev/null 2>&1 || { echo "Docker is required"; exit 1; }
command -v docker-compose >/dev/null 2>&1 || { echo "Docker Compose is required"; exit 1; }

# Start with embedded client (default)
echo "📦 Starting with embedded client in API wwwroot..."
docker-compose -f docker-compose.yml up --build -d

# Wait for services to be healthy
echo "⏳ Waiting for services to be ready..."
timeout 120 bash -c '
  while ! docker-compose ps | grep -q "healthy"; do
    echo "  Waiting for health checks..."
    sleep 5
  done
'

echo "✅ S13.DocMind is ready!"
echo "🌐 Web Interface: http://localhost:4925"
echo "📚 API Documentation: http://localhost:4925/swagger"
echo "🔍 Health Check: http://localhost:4925/health"

# Optional: Start with separate client
if [ "$1" = "--separate-client" ]; then
    echo "🔄 Starting with separate client container..."
    docker-compose -f docker-compose.separate-client.yml up --build -d
    echo "🌐 API: http://localhost:4926"
    echo "🌐 Client: http://localhost:4927"
fi
```

##### **stop.sh - Cleanup Script**
```bash
#!/bin/bash
# samples/S13.DocMind/stop.sh

echo "🛑 Stopping S13.DocMind..."

docker-compose -f docker-compose.yml down
docker-compose -f docker-compose.separate-client.yml down 2>/dev/null || true

if [ "$1" = "--clean" ]; then
    echo "🧹 Cleaning up volumes and images..."
    docker-compose -f docker-compose.yml down -v --rmi local
    docker system prune -f
fi

echo "✅ S13.DocMind stopped"
```

#### **Container Orchestration (Production)**

The primary sample compose file (`docker-compose.yml`) boots the simplified stack—MongoDB for data, Weaviate for optional vector embeddings, and Ollama for AI processing—alongside the API container. This minimal approach demonstrates core Koan Framework patterns without unnecessary infrastructure complexity.
```yaml
# docker-compose.production.yml
version: '3.8'
services:
  docmind-api:
    build:
      context: .
      dockerfile: Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Koan__Data__Providers__mongodb__connectionString=mongodb://mongo-primary:27017,mongo-secondary:27017/s13docmind?replicaSet=rs0
      - Koan__Data__Providers__weaviate__endpoint=http://weaviate:8080
      - Koan__AI__OpenAI__ApiKey=${OPENAI_API_KEY}
      - Koan__AI__Ollama__BaseUrl=http://ollama:11434
    ports: ["8080:8080"]
    depends_on:
      - mongo-primary
      - weaviate
      - ollama
    deploy:
      replicas: 3
      resources:
        limits: {cpus: '2.0', memory: 4G}
        reservations: {cpus: '1.0', memory: 2G}
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s

  # MongoDB replica set for production
  mongo-primary:
    image: mongo:7.0
    command: mongod --replSet rs0 --bind_ip_all
    volumes: ["mongo-primary-data:/data/db"]

  mongo-secondary:
    image: mongo:7.0
    command: mongod --replSet rs0 --bind_ip_all
    volumes: ["mongo-secondary-data:/data/db"]

  # Weaviate cluster
  weaviate:
    image: semitechnologies/weaviate:1.22.4
    environment:
      QUERY_DEFAULTS_LIMIT: 25
      AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED: 'false'
      AUTHENTICATION_OIDC_ENABLED: 'true'
      PERSISTENCE_DATA_PATH: '/var/lib/weaviate'
      DEFAULT_VECTORIZER_MODULE: 'none'
      ENABLE_MODULES: 'backup-filesystem,offload-s3'
      CLUSTER_HOSTNAME: 'node1'
    volumes: ["weaviate-data:/var/lib/weaviate"]

  # Ollama for local AI
  ollama:
    image: ollama/ollama:latest
    volumes: ["ollama-models:/root/.ollama"]
    environment:
      - OLLAMA_MODELS_DIR=/root/.ollama
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: 1
              capabilities: [gpu]

volumes:
  mongo-primary-data:
  mongo-secondary-data:
  weaviate-data:
  ollama-models:
```

#### **Health Monitoring Specification**
```csharp
namespace S13.DocMind.Health
{
    public class DocMindHealthCheck : IHealthCheck
    {
        private readonly KoanOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IObjectStorageClient _storage;

        public DocMindHealthCheck(
            IOptions<KoanOptions> options,
            IHttpClientFactory httpClientFactory,
            IObjectStorageClient storage)
        {
            _options = options.Value;
            _httpClientFactory = httpClientFactory;
            _storage = storage;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        {
            var checks = new List<(string name, Func<Task<bool>> check)>
            {
                ("MongoDB", CheckMongoHealthAsync),
                ("Weaviate", CheckWeaviateHealthAsync),
                ("Ollama", CheckOllamaHealthAsync),
                ("Storage Provider", CheckObjectStorageHealthAsync),
                ("Document Processing", CheckProcessingHealthAsync)
            };

            if (IsAiProviderEnabled("openai"))
            {
                checks.Add(("OpenAI", CheckOpenAiHealthAsync));
            }

            var results = await Task.WhenAll(checks.Select(async c => new
            {
                c.name,
                result = await c.check()
            }));
            var failures = results.Where(r => !r.result).ToList();

            if (failures.Any())
            {
                var failureNames = string.Join(", ", failures.Select(f => f.name));
                return HealthCheckResult.Unhealthy($"Failed components: {failureNames}");
            }

            return HealthCheckResult.Healthy("All systems operational");
        }

        private async Task<bool> CheckMongoHealthAsync()
        {
            try
            {
                _ = await Document.Take(1);
                return true;
            }
            catch { return false; }
        }

        private async Task<bool> CheckAiHealthAsync()
        {
            try
            {
                var response = await AI.Prompt("Test")
                    .WithTimeout(TimeSpan.FromSeconds(10))
                    .ExecuteAsync();
                return !string.IsNullOrEmpty(response.Content);
            }
            catch { return false; }
        }

        private async Task<bool> CheckOllamaHealthAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient("ollama-health");
                var response = await client.GetAsync("/api/tags");
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private async Task<bool> CheckObjectStorageHealthAsync()
        {
            try
            {
                await _storage.EnsureBucketExistsAsync("documents");
                return true;
            }
            catch { return false; }
        }

        private async Task<bool> CheckOpenAiHealthAsync()
        {
            try
            {
                var response = await AI.Prompt("ping")
                    .WithProvider("openai")
                    .WithTimeout(TimeSpan.FromSeconds(5))
                    .ExecuteAsync();
                return !string.IsNullOrEmpty(response.Content);
            }
            catch { return false; }
        }

        private bool IsProviderEnabled(string providerKey)
            => _options.Data?.Providers?.ContainsKey(providerKey) == true;

        private bool IsAiProviderEnabled(string providerKey)
            => _options.AI?.ContainsKey(providerKey) == true;
    }
}
```


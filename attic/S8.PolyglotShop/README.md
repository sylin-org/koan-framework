# PolyglotShop - Multi-Language E-Commerce Platform

**A sample e-commerce application showcasing Koan Services with AI-powered translation and service mesh architecture.**

![Status](https://img.shields.io/badge/status-sample-blue)
![Framework](https://img.shields.io/badge/koan-v1.0-blue)
![Architecture](https://img.shields.io/badge/architecture-microservices-green)

---

## What is PolyglotShop?

PolyglotShop demonstrates **Koan Services** - a composable, modular service architecture for building distributed applications. It showcases:

✨ **Reference = Intent** - Add `Koan.Services.Translation` package → translation just works
🔗 **Dual Deployment** - Services run in-process (NuGet) or containerized (Docker) with same code
🌐 **Service Discovery** - UDP multicast-based auto-discovery via orchestrator channel
⚡ **Smart Routing** - Automatic routing between in-process and remote service instances
🎯 **Static Service API** - `Translation.Translate()` matches Entity/Vector patterns
🏗️ **Three-Tier Architecture** - Orchestrator (discovery) + Service channels (pub/sub) + HTTP (invocation)

## Quick Start

**Prerequisites**: Docker Desktop (manages Ollama and Translation service automatically)

```bash
# Clone the repository
git clone https://github.com/your-org/koan-framework.git
cd koan-framework/samples/S8.PolyglotShop

# Run the start script (handles everything automatically)
./start.bat  # Windows
```

The script automatically:
1. Builds the Docker images
2. Starts all required services (Ollama, Translation service)
3. Initializes the application
4. Opens your browser to http://localhost:5080

**That's it!** Test translation endpoints and see service discovery in action.

---

## What You'll Learn

This sample demonstrates **Koan Services** patterns:

### 🔗 Static Service API (SERV-0001)

Use services with Entity-like static methods:

```csharp
// Static API matches Entity.Get() and Vector.Search() patterns
var result = await Translation.Translate(
    "Hello world",
    targetLanguage: "es"
);

// result.TranslatedText: "Hola mundo"
// result.DetectedSourceLanguage: "en"
```

**Why it matters**: Consistent API across all Koan pillars (Data, Vector, Services).

### 🎯 Service Discovery & Routing

Services auto-discover via UDP multicast:

```csharp
// [KoanService] attribute enables auto-discovery
[KoanService(
    "translation",
    Port = 8080,
    HeartbeatIntervalSeconds = 30)]
public class TranslationService
{
    [KoanCapability("translate")]
    public async Task<TranslationResult> Translate(
        TranslationOptions options,
        CancellationToken ct = default)
    {
        // Implementation...
    }
}
```

**ServiceExecutor** routes to in-process or remote:
1. **In-Process**: Service runs in same process (low latency)
2. **Remote**: Service runs in container (auto-discovered via orchestrator channel)

### 🌐 Three-Tier Communication

**Tier 1: Orchestrator Channel** (239.255.42.1:42001)
- Global discovery channel - ALL services join
- Announcement protocol with heartbeats (default: 30s)
- Discovery requests/responses

**Tier 2: Service Channels** (Optional)
- Per-service multicast for pub/sub coordination
- Example: Translation service could have 239.255.42.10:42010 for coordinating all translation instances

**Tier 3: HTTP Endpoints**
- Per-instance REST APIs for actual invocations
- Translation: `POST /api/translation/translate`

### 📦 Dual Deployment Model

**Option 1: In-Process (NuGet)**
```xml
<PackageReference Include="Koan.Services.Translation" />
```
- Service runs in same process
- Zero latency
- Perfect for development

**Option 2: Containerized (Docker)**
```yaml
translation-service:
  image: koan/service-translation:latest
  # Automatically discovered by applications
```
- Service runs in separate container
- Easy horizontal scaling
- Production deployment

**Same code, different deployment!**

---

## Usage Examples

### Basic Translation

```bash
curl -X POST http://localhost:5080/translate \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Hello world",
    "targetLanguage": "es"
  }'
```

Response:
```json
{
  "originalText": "Hello world",
  "translatedText": "Hola mundo",
  "detectedSourceLanguage": "en",
  "targetLanguage": "es",
  "confidence": 0.95
}
```

### Language Detection

```bash
curl -X POST http://localhost:5080/detect-language \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Bonjour le monde"
  }'
```

Response:
```json
{
  "text": "Bonjour le monde",
  "detectedLanguage": "fr",
  "confidence": 0.90
}
```

---

## Architecture Highlights

### Service Discovery Flow

1. **Startup**: Translation service announces to orchestrator channel (239.255.42.1:42001)
2. **Discovery**: PolyglotShop broadcasts discovery request on orchestrator channel
3. **Response**: Translation service responds with instance info (HTTP endpoint, capabilities)
4. **Invocation**: PolyglotShop routes to discovered instance (or in-process if available)
5. **Heartbeat**: Translation service sends periodic announcements (default: 30s)
6. **Cleanup**: Stale instances removed after threshold (default: 120s)

### Configuration Hierarchy

Services follow Koan's configuration hierarchy:

1. **Attribute Defaults** (lowest priority)
2. **Attribute Explicit Values**
3. **appsettings.json**
4. **Environment Variables** (highest priority)

Example:
```csharp
[KoanService(
    "translation",
    Port = 8080,  // Can be overridden via appsettings or env vars
    HeartbeatIntervalSeconds = 30)]
```

Override in appsettings.json:
```json
{
  "Koan": {
    "Services": {
      "Translation": {
        "Port": 9090
      }
    }
  }
}
```

### Load Balancing

When multiple service instances are available:

```csharp
// Round-robin (default)
var result = await Translation.Translate(text, "es");

// Explicit policy
var result = await Translation.Translate(
    text,
    "es",
    policy: LoadBalancingPolicy.LeastConnections);
```

**Available policies:**
- `RoundRobin`: Distribute evenly across instances
- `Random`: Random selection
- `LeastConnections`: Route to instance with fewest active requests
- `HealthAware`: Route to fastest instance based on response time

---

## Project Structure

```
S8.PolyglotShop/
├── Program.cs                    # Application entry point
├── appsettings.json             # Configuration
├── S8.PolyglotShop.csproj       # Project file with Koan references
├── Dockerfile                   # Container build
├── start.bat                    # Docker Compose launcher
├── docker/
│   └── compose.yml              # Multi-service orchestration
└── .Koan/                       # Data persistence
    ├── Data/                    # Ollama models
    └── cache/                   # AI cache
```

---

## Running in Development

**Option 1: Docker Compose (Recommended)**
```bash
./start.bat  # Runs all services
```

**Option 2: Local Development**
```bash
# Start Ollama separately
docker run -d -p 11434:11434 ollama/ollama:latest

# Run the app
cd samples/S8.PolyglotShop
dotnet run
```

Translation service runs in-process - no separate container needed!

---

## Debugging

Enable detailed Koan Services logging:

```json
{
  "Logging": {
    "LogLevel": {
      "Koan.Services": "Debug"
    }
  }
}
```

**Log patterns:**
```
[INFO] Koan:services:mesh joined orchestrator channel 239.255.42.1:42001
[INFO] Koan:services:coordinator broadcasting discovery request
[INFO] Koan:services:mesh discovered translation instance abc123 at http://172.18.0.3:8080
[INFO] Koan:services:execute translation.translate completed in 250ms (remote abc123)
```

---

## See Also

- **ADR**: [SERV-0001: Koan Services Architecture](../../docs/decisions/SERV-0001-koan-services-architecture.md)
- **Translation Service**: [Koan.Services.Translation](../../src/Koan.Services.Translation/)
- **Service Mesh**: [Koan.Services](../../src/Koan.Services/)

---

## Technology Stack

- **ASP.NET Core 10** - Web framework
- **Koan Framework** - Entity-first architecture
- **Koan Services** - Microservice discovery and routing
- **UDP Multicast** - Service discovery protocol
- **Ollama** - AI-powered translation
- **Docker** - Containerization

---

## Production Considerations

### Scaling Translation Service

```bash
# Scale to 3 instances
docker compose up -d --scale translation-service=3

# Load balancing happens automatically via service mesh
```

### Monitoring

Service mesh exposes metrics via structured logging:
- Instance discovery events
- Load balancing decisions
- Request routing (in-process vs remote)
- Response times per instance
- Connection counts

### Security

For production:
- Add authentication to HTTP endpoints
- Use VPN/private network for UDP multicast
- Implement rate limiting
- Add TLS for HTTP communication

---

**Built with Koan Framework** - https://github.com/your-org/koan-framework

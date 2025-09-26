# S13.DocMind: AI-Native Document Intelligence Platform

**S13.DocMind** is a guided sample showcasing how the Koan Framework integrates data, AI, and web capabilities to build an AI-native document intelligence experience. This sample demonstrates entity-first development, multi-provider architecture, and sophisticated AI-powered document analysis.

## 🌟 Key Features

- **Multi-format Document Processing**: PDF, DOCX, TXT, and images with intelligent text extraction
- **AI-Powered Analysis**: Document information extraction using configurable templates
- **Multi-Modal Support**: Both text and vision model integration for comprehensive analysis
- **Template System**: User-defined document types with AI-generated analysis templates
- **Vector Similarity Search**: Semantic document type matching and similar document discovery
- **Minimal Multi-Provider Architecture**: MongoDB for persistence with Weaviate-powered vector search and Ollama-backed models
- **Background Processing**: Async document analysis with real-time status tracking
- **Auto-Generated APIs**: Full CRUD operations via Koan's `EntityController<T>`

## 🏗️ Architecture Overview

### Entity-First Design
- **File**: Raw content + extracted text with processing state tracking
- **DocumentType**: AI analysis templates with vector embeddings for matching
- **Analysis**: AI-generated results with confidence scoring and structured data

### Multi-Provider Data Strategy
- **MongoDB**: Primary document storage (metadata + extracted text)
- **Weaviate**: Vector operations for similarity search
- **Ollama**: Local AI provider for text, vision, and embedding workloads

### Processing Pipeline
```
Upload → Extract Text → User Assigns Type → Background AI Analysis → Completed
```

## 🚀 Quick Start

### Prerequisites
- Docker & Docker Compose
- .NET 8 SDK (optional, for local development)
- OpenAI API key (optional, defaults to Ollama)

### 1. Start Infrastructure & API

```powershell
# Start everything (Windows)
./start.bat
```

```bash
# Start everything (macOS/Linux)
docker compose -f docker-compose.yml up -d
```

This will:
- Start MongoDB, Weaviate, and Ollama containers alongside the API
- Initialize the S13.DocMind API (locally or containerized)
- Create default document types

### 2. Access the Platform

- **API**: http://localhost:5000
- **API Documentation**: http://localhost:5000/api/docs
- **Health Check**: http://localhost:5000/health

### 3. Basic Usage Flow

1. **Upload a document**:
   ```bash
   curl -X POST http://localhost:5000/api/files/upload \
     -F "file=@your-document.pdf"
   ```

2. **Assign a document type**:
   ```bash
   curl -X PUT http://localhost:5000/api/files/{fileId}/assign-type \
     -H "Content-Type: application/json" \
     -d '{"typeId": "{documentTypeId}"}'
   ```

3. **Check analysis status**:
   ```bash
   curl http://localhost:5000/api/files/{fileId}/status
   ```

4. **Retrieve analysis results**:
   ```bash
   curl http://localhost:5000/api/files/{fileId}/analysis
   ```

## 📖 API Endpoints

### Files
- `POST /api/files/upload` - Upload and process documents
- `GET /api/files/{id}/status` - Check processing status
- `PUT /api/files/{id}/assign-type` - Assign document type (triggers AI analysis)
- `GET /api/files/{id}/analysis` - Get analysis results
- `GET /api/files/{id}/similar-types` - Find similar document types

### Document Types
- `GET /api/document-types` - List all document types
- `POST /api/document-types/generate` - AI-generate new document type
- `GET /api/document-types/{id}/files` - Files assigned to type
- `POST /api/document-types/initialize-defaults` - Create default types

### AI Model Management
- `GET /api/models/available` - Browse available AI models
- `GET /api/models/installed` - List installed models
- `POST /api/models/{modelName}/install` - Install model
- `GET /api/models/config` - Current model configuration
- `PUT /api/models/text-model` - Set active text model
- `PUT /api/models/vision-model` - Set active vision model

### Analysis
- `GET /api/analysis/by-file/{fileId}` - Get file analysis
- `GET /api/analysis/high-confidence` - High-quality results
- `GET /api/analysis/stats` - Analysis statistics
- `POST /api/analysis/{id}/regenerate` - Regenerate analysis

## 🔧 Configuration

### Environment Variables
```bash
# AI Configuration
OPENAI_API_KEY=your-openai-key
Koan__AI__Providers__ollama__baseUrl=http://localhost:11434

# Database Configuration
Koan__Data__Providers__mongodb__connectionString=mongodb://localhost:27017
Koan__Data__Providers__weaviate__endpoint=http://localhost:8080

# File Storage
S13__DocMind__StorageRoot=./storage
S13__DocMind__MaxFileSizeBytes=52428800
```

### Supported File Types
- **Text**: `.txt`
- **PDF**: `.pdf` (text extraction)
- **Word**: `.docx` (text extraction)
- **Images**: `.png`, `.jpg`, `.gif`, `.bmp` (OCR via vision models)

## 🤖 AI Model Management

### Default Models
- **Text Analysis**: `gpt-4-turbo`
- **Vision Analysis**: `gpt-4-vision-preview`
- **Embeddings**: `text-embedding-3-large`

### Ollama Integration
The platform supports local Ollama models for offline/private deployments:

1. **Install Ollama models**:
   ```bash
   # Via API
   curl -X POST http://localhost:5000/api/models/llama3/install

   # Direct to Ollama
   docker exec s13-docmind-ollama ollama pull llama3
   ```

2. **Switch to local models**:
   ```bash
   curl -X PUT http://localhost:5000/api/models/text-model \
     -H "Content-Type: application/json" \
     -d '{"modelName": "llama3", "provider": "ollama"}'
   ```

## 🏛️ Architecture Principles

### Koan Framework Benefits
- **"Reference = Intent"**: Adding provider packages auto-enables capabilities
- **Entity-First**: `File.Get(id)`, `file.Save()` patterns with GUID v7 auto-generation
- **Provider Transparency**: Same entity code works across MongoDB today with clear seams for future providers
- **Auto-Registration**: Single `AddKoan()` call replaces 60+ lines of manual DI
- **Self-Reporting**: Structured boot reports describe system capabilities

### No-Flow Architecture
This sample demonstrates Koan without Flow dependency:
- **Background Processing**: `IDocumentProcessingService` with async queues
- **State Tracking**: Direct entity state management vs. event sourcing
- **Simplified Deployment**: Fewer moving parts while maintaining functionality

## 📊 Monitoring & Observability

### Health Checks
- **API Health**: http://localhost:5000/health
- **Service Status**: `docker-compose ps`
- **Logs**: `docker-compose logs -f`

### Built-in Analytics
- **File Statistics**: Processing success rates, file counts
- **Analysis Quality**: Confidence scores, model performance
- **Token Usage**: Input/output token tracking per model
- **Processing Times**: Performance monitoring

## 🧪 Development

### Local Development
```bash
# Start infrastructure only
docker-compose up -d

# Run API locally
export ASPNETCORE_ENVIRONMENT=Development
dotnet run

# Watch for changes
dotnet watch run
```

### Testing
```bash
# Run tests (when implemented)
dotnet test

# Integration test with real services
./start.sh
curl http://localhost:5000/health
```

## 🛑 Shutdown

```powershell
# Stop everything
./stop.ps1

# Remove all data (destructive)
docker-compose down -v
```

## 📝 Sample Document Types

The system initializes with three default document types:

1. **Meeting Notes** (`MEETING`)
   - Extracts attendees, key points, decisions, action items

2. **Technical Specification** (`TECH_SPEC`)
   - Captures requirements, architecture, implementation details

3. **Feature Request** (`FEATURE`)
   - Identifies description, business value, acceptance criteria

## 🔮 Advanced Features

### Custom Document Types
Generate new document types via AI:
```bash
curl -X POST http://localhost:5000/api/document-types/generate \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Create a document type for invoice processing"}'
```

### Vector Similarity Search
Find similar documents or types:
```bash
curl http://localhost:5000/api/files/{fileId}/similar-types?threshold=0.8
```

### Batch Processing
The system handles concurrent document processing with configurable limits and automatic retry logic.

---

**S13.DocMind** demonstrates production-ready patterns for AI-native document intelligence using the Koan Framework's powerful abstractions and multi-provider architecture.
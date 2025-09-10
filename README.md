# Sora Framework

**Build services like you're talking to your code, not fighting it.**

Sora is a modular .NET backend framework for developers who value clarity, comfort, and the ability to grow. Whether you're spinning up a quick prototype or scaling into enterprise-grade patterns, Sora keeps the path clear.

## 🚀 Quick Start

```bash
# Create project
dotnet new web && dotnet add package Sora.Core Sora.Web Sora.Data.Sqlite

# Add model
echo 'public class Todo : Entity<Todo> { public string Title { get; set; } = ""; }' > Todo.cs

# Add controller  
echo '[Route("api/[controller]")] public class TodosController : EntityController<Todo> { }' > TodosController.cs

# Run
dotnet run
```

**That's a full REST API.** Visit `/swagger` to explore.

## 📚 Documentation

- **[5-Minute Quickstart](docs/quickstart.md)** - Get running now
- **[Complete Documentation](docs/)** - Comprehensive guides and reference
- **[Framework Overview](docs/reference/framework-overview.md)** - Architecture and capabilities
- **[Getting Started Guide](docs/reference/getting-started.md)** - Full walkthrough

## 🌱 Key Features

- **Start Simple**: Real service in a single file
- **Entity-First**: Models drive your API automatically  
- **Modular**: Add AI, messaging, vector search as you grow
- **Production-Ready**: Health checks, observability, security built-in
- **No Lock-in**: Escape hatches everywhere

## 🧱 Pillars

| Pillar | Purpose |
|--------|---------|
| **Core** | Runtime, health checks, configuration |
| **Data** | Unified access to SQL, NoSQL, vector DBs |
| **Web** | REST + GraphQL from your models |
| **AI** | Chat, embeddings, vector search, RAG |
| **Messaging** | Reliable queues and event patterns |
| **Storage** | File/blob handling with profiles |
| **Media** | First-class media with HTTP endpoints |
| **Flow** | Data pipeline and ingestion |

## 🛠️ CLI & Orchestration

```bash
# Install CLI
./scripts/cli-all.ps1

# Export Docker Compose for local dependencies
Sora export compose --profile Local

# Run dependencies with health checks
Sora up --profile Local --timeout 300
```

## 📦 Requirements

- **.NET 9 SDK**
- **Current Version**: v0.2.18

## 🤝 Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## 📄 License

Apache 2.0 - See [LICENSE](LICENSE) for details.
# Koan Framework Module Discovery

## Overview

This document provides a complete inventory of all Koan Framework modules discovered in the codebase. There are **63 total modules** organized into distinct functional categories.

## Module Categories

### Core Framework (5 modules)
- **Koan.Core** - Core framework infrastructure and patterns
- **Koan.Diagnostics.Core** - Diagnostic and monitoring infrastructure
- **Koan.Diagnostics.Tool** - Diagnostic tooling and utilities
- **Koan.Recipe.Abstractions** - Recipe pattern abstractions
- **Koan.Recipe.Observability** - Observability recipe implementations

### Data Layer (16 modules)
#### Core Data Infrastructure
- **Koan.Data.Abstractions** - Data layer abstractions and interfaces
- **Koan.Data.Core** - Core data infrastructure and patterns
- **Koan.Data.Direct** - Direct data access patterns
- **Koan.Data.Cqrs** - Command Query Responsibility Segregation support
- **Koan.Data.Cqrs.Outbox.Mongo** - MongoDB outbox pattern for CQRS

#### Relational Database Providers
- **Koan.Data.Relational** - Common relational database infrastructure
- **Koan.Data.Postgres** - PostgreSQL provider
- **Koan.Data.Sqlite** - SQLite provider
- **Koan.Data.SqlServer** - SQL Server provider

#### NoSQL and Alternative Storage
- **Koan.Data.Mongo** - MongoDB provider
- **Koan.Data.Redis** - Redis provider
- **Koan.Data.Json** - JSON file-based storage

#### Vector Database and AI Storage
- **Koan.Data.Vector** - Vector database core functionality
- **Koan.Data.Vector.Abstractions** - Vector storage abstractions
- **Koan.Data.Weaviate** - Weaviate vector database provider

### Web Framework (13 modules)
#### Core Web Infrastructure
- **Koan.Web** - Core web framework and ASP.NET Core integration
- **Koan.Web.Extensions** - Common web extensions and utilities
- **Koan.Web.Diagnostics** - Web-specific diagnostics and health checks
- **Koan.Web.Transformers** - Request/response transformation middleware

#### Authentication and Authorization
- **Koan.Web.Auth** - Core authentication infrastructure
- **Koan.Web.Auth.Services** - Service-to-service authentication (OAuth 2.0)
- **Koan.Web.Auth.TestProvider** - Development OAuth provider
- **Koan.Web.Auth.Roles** - Role-based authorization
- **Koan.Web.Auth.Oidc** - OpenID Connect provider
- **Koan.Web.Auth.Google** - Google OAuth integration
- **Koan.Web.Auth.Microsoft** - Microsoft OAuth integration
- **Koan.Web.Auth.Discord** - Discord OAuth integration

#### API and Documentation
- **Koan.Web.GraphQl** - GraphQL integration and support
- **Koan.Web.Swagger** - OpenAPI/Swagger documentation

### AI and Machine Learning (4 modules)
- **Koan.AI** - Core AI infrastructure and abstractions
- **Koan.AI.Contracts** - AI service contracts and DTOs
- **Koan.AI.Web** - Web integration for AI services
- **Koan.Ai.Provider.Ollama** - Ollama AI provider integration

### Event Streaming and Flow (4 modules)
- **Koan.Flow.Core** - Event sourcing and flow orchestration core
- **Koan.Flow.Web** - Web integration for flow operations
- **Koan.Flow.RabbitMq** - RabbitMQ integration for flow events
- **Koan.Flow.Runtime.Dapr** - Dapr runtime integration for distributed flows

### Messaging (5 modules)
- **Koan.Messaging.Abstractions** - Messaging abstractions and patterns
- **Koan.Messaging.Core** - Core messaging infrastructure
- **Koan.Messaging.RabbitMq** - RabbitMQ messaging provider
- **Koan.Messaging.Inbox.Http** - HTTP-based inbox pattern
- **Koan.Messaging.Inbox.InMemory** - In-memory inbox for development

### Media Processing (3 modules)
- **Koan.Media.Abstractions** - Media processing abstractions
- **Koan.Media.Core** - Core media processing functionality
- **Koan.Media.Web** - Web integration for media operations

### Storage (2 modules)
- **Koan.Storage** - Storage abstractions and core functionality
- **Koan.Storage.Local** - Local file system storage provider

### Security and Secrets (3 modules)
- **Koan.Secrets.Abstractions** - Secret management abstractions
- **Koan.Secrets.Core** - Core secret management functionality
- **Koan.Secrets.Vault** - HashiCorp Vault integration

### Container Orchestration (6 modules)
- **Koan.Orchestration.Abstractions** - Container orchestration abstractions
- **Koan.Orchestration.Cli** - Command-line orchestration tools
- **Koan.Orchestration.Generators** - Configuration generators
- **Koan.Orchestration.Provider.Docker** - Docker provider
- **Koan.Orchestration.Provider.Podman** - Podman provider
- **Koan.Orchestration.Renderers.Compose** - Docker Compose renderer

### Infrastructure and Services (2 modules)
- **Koan.Scheduling** - Background job scheduling
- **Koan.Service.Inbox.Redis** - Redis-based service inbox pattern

## Module Dependency Patterns

### Layered Architecture
The modules follow a clear layered architecture:
1. **Core Layer**: `Koan.Core` provides foundational patterns
2. **Abstraction Layer**: `*.Abstractions` modules define contracts
3. **Implementation Layer**: Provider-specific implementations
4. **Integration Layer**: Web and framework-specific integrations

### Provider Pattern
Many modules follow a provider pattern:
- **Abstractions**: Define interfaces and contracts
- **Core**: Provide common functionality and base implementations
- **Providers**: Implement specific technology integrations (Docker, Podman, PostgreSQL, etc.)

### Cross-Cutting Concerns
Several modules provide cross-cutting functionality:
- **Diagnostics**: Monitoring and health checks across all layers
- **Authentication**: Security across web and service layers
- **Configuration**: Settings and options management
- **Messaging**: Event-driven communication between modules

## Next Steps

This discovery provides the foundation for deep analysis of each module's capabilities, architecture patterns, and integration approaches in the next phase of documentation.
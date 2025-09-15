---
title: Orchestration Manifest Generator — Getting Services Running Automatically
description: How to add simple attributes to your code so Koan can automatically start the databases and services your app needs
---

# Orchestration Manifest Generator — Getting Services Running Automatically

## What is this and why do I need it?

The Koan Framework automatically discovers the services your application depends on (like databases, message queues, vector stores) and generates the Docker configuration needed to run them locally or in containers. Instead of manually writing docker-compose files, you just add simple attributes to your data adapter classes, and Koan handles the rest.

This system solves several problems:
- **No more manual docker-compose maintenance** - Your service configuration lives with your code
- **Consistent environments** - Development, testing, and production use the same service definitions
- **Automatic dependency discovery** - Koan finds all the services you actually use
- **Zero-configuration local development** - Just run `Koan up` and everything starts

## How it works (the big picture)

Here's what happens when you build and run your Koan application:

### 1. Build Time (Automatic)
When you build your project, a source generator scans your code for orchestration attributes and creates a manifest file containing all your service definitions. This happens completely automatically - you don't need to do anything special.

### 2. Runtime Discovery (When you run `Koan up`)
When you run Koan CLI commands, the system:
1. Looks for the generated manifest in your built assemblies
2. Reads all the service definitions you've declared
3. Builds a deployment plan based on your target profile (Local vs Container)
4. Generates a docker-compose.yml file with all dependencies
5. Starts everything with proper health checks and startup ordering

### 3. Your App Runs
Your application gets environment variables pointing to the running services, and everything just works.

## Quick Start: Adding a Database

Let's say you're adding PostgreSQL to your application. Here's the complete process:

### Step 1: Add attributes to your data adapter

```csharp
using Koan.Orchestration.Abstractions.Attributes;

[ServiceId("postgresql")]
[ContainerDefaults("postgres:16", 
    Ports = new[] { 5432 },
    Env = new[] { 
        "POSTGRES_DB=myapp", 
        "POSTGRES_USER=user", 
        "POSTGRES_PASSWORD=password" 
    },
    Volumes = new[] { "/var/lib/postgresql/data" })]
[EndpointDefaults(EndpointMode.Container, "tcp", "postgres", 5432)]
[EndpointDefaults(EndpointMode.Local, "tcp", "localhost", 5432)]
[AppEnvDefaults(new[] { "DATABASE_URL=postgres://{user}:{password}@{host}:{port}/{database}" })]
[HealthEndpointDefaults("/", IntervalSeconds = 30, TimeoutSeconds = 10, Retries = 3)]
public class PostgresDataAdapter : IDataAdapter
{
    // Your implementation here...
}
```

### Step 2: Build your project
```bash
dotnet build
```

That's it! The generator automatically creates a manifest containing your PostgreSQL configuration.

### Step 3: Run your application
```bash
Koan up
```

Koan will:
- Start PostgreSQL in a Docker container
- Wait for it to be healthy
- Set the `DATABASE_URL` environment variable for your app
- Start your application

## Understanding the Attributes

Each attribute serves a specific purpose in the orchestration process:

### ServiceId - "What should we call this service?"
```csharp
[ServiceId("postgresql")]
```
This gives your service a unique name that Koan uses internally. Keep it simple and descriptive.

### ContainerDefaults - "How do we run this in Docker?"
```csharp
[ContainerDefaults("postgres:16", 
    Ports = new[] { 5432 },
    Env = new[] { "POSTGRES_DB=myapp" },
    Volumes = new[] { "/var/lib/postgresql/data" })]
```
This tells Koan:
- **Image**: What Docker image to use (`postgres:16`)
- **Ports**: What ports the service listens on (`5432`)
- **Env**: Environment variables the container needs
- **Volumes**: Where to store persistent data

### EndpointDefaults - "How does my app connect to this service?"
```csharp
[EndpointDefaults(EndpointMode.Container, "tcp", "postgres", 5432)]
[EndpointDefaults(EndpointMode.Local, "tcp", "localhost", 5432)]
```
You need both modes:
- **Container mode**: How services talk to each other inside Docker (`postgres:5432`)
- **Local mode**: How your app connects during development (`localhost:5432`)

### AppEnvDefaults - "What environment variables should my app get?"
```csharp
[AppEnvDefaults(new[] { "DATABASE_URL=postgres://{user}:{password}@{host}:{port}/{database}" })]
```
Koan will create environment variables for your application using the connection details. The `{tokens}` get replaced with actual values.

### HealthEndpointDefaults - "How do we know the service is ready?"
```csharp
[HealthEndpointDefaults("/health", IntervalSeconds = 30, TimeoutSeconds = 10, Retries = 3)]
```
This tells Docker how to check if your service is healthy before starting dependent services. Only use this for HTTP services.

## Real Examples from the Koan Codebase

### Weaviate Vector Database
```csharp
[ServiceId("weaviate")]
[ContainerDefaults("weaviate/weaviate:1.22.4",
    Ports = new[] { 8080 },
    Env = new[] {
        "QUERY_DEFAULTS_LIMIT=25",
        "AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED=true",
        "PERSISTENCE_DATA_PATH=/var/lib/weaviate",
        "DEFAULT_VECTORIZER_MODULE=none",
        "CLUSTER_HOSTNAME=node1"
    },
    Volumes = new[] { "/var/lib/weaviate" })]
[EndpointDefaults(EndpointMode.Container, "http", "weaviate", 8080)]
[EndpointDefaults(EndpointMode.Local, "http", "localhost", 8080)]
[AppEnvDefaults(new[] { "WEAVIATE_URL=http://{host}:{port}" })]
[HealthEndpointDefaults("/v1/.well-known/ready", IntervalSeconds = 30, TimeoutSeconds = 10, Retries = 3)]
public class WeaviateVectorSearchAdapter : IVectorSearchAdapter
```

### Redis Cache
```csharp
[ServiceId("redis")]
[ContainerDefaults("redis:7-alpine",
    Ports = new[] { 6379 },
    Volumes = new[] { "/data" })]
[EndpointDefaults(EndpointMode.Container, "tcp", "redis", 6379)]
[EndpointDefaults(EndpointMode.Local, "tcp", "localhost", 6379)]
[AppEnvDefaults(new[] { "REDIS_URL=redis://{host}:{port}" })]
public class RedisDataAdapter : IDataAdapter
```

Notice that Redis doesn't have a health check because it's not an HTTP service.

## What Happens Under the Hood

### During Build (Source Generator)
The `OrchestrationManifestGenerator` source generator:
1. Scans all classes in your project for orchestration attributes
2. Extracts the service configuration from each attribute
3. Generates a C# file with embedded JSON containing all service definitions
4. This file gets compiled into your assembly

The generated file looks like this:
```csharp
namespace Koan.Orchestration
{
    internal static class __KoanOrchestrationManifest
    {
        public const string Json = @"{""services"":[{""id"":""postgresql"",""image"":""postgres:16""...}]}";
    }
}
```

### During Discovery (CLI Runtime)
When you run `Koan up`, the `ProjectDependencyAnalyzer`:
1. Finds your built assembly files
2. Looks for the `__KoanOrchestrationManifest` class
3. Reads the embedded JSON string
4. Parses all service definitions
5. Falls back to reflection if no generated manifest is found

### During Planning
The `Planner`:
1. Takes the discovered services
2. Applies any overrides from descriptor files
3. Chooses the right endpoint mode (Container vs Local)
4. Builds a complete deployment plan

### During Export (Compose Generation)
The `ComposeExporter`:
1. Takes the deployment plan
2. Generates docker-compose.yml services
3. Adds health checks for HTTP services
4. Sets up proper startup dependencies
5. Configures volume mounts and networks

## When and How to Use This

### As a Data Adapter Author
You should add orchestration attributes to your adapter classes whenever:
- Your adapter connects to an external service (database, cache, queue, etc.)
- That service can run in a Docker container
- Developers using your adapter need that service for local development

### As a Service Provider
If you're creating a new type of data adapter or service integration:
1. Add the orchestration attributes to your service class
2. Test that `Koan up` correctly starts your service
3. Verify that your service's health check works
4. Document any special configuration requirements

### As an Application Developer
You typically don't need to add these attributes to your own application code. The Koan data adapters you're using should already have them. You just:
1. Add the data adapter NuGet packages to your project
2. Run `Koan up` to start everything
3. Your app gets the right environment variables automatically

## Integration with NuGet Packages

### How Attributes Travel in NuGet Packages
When you create a NuGet package containing data adapters with orchestration attributes:
1. The attributes are compiled into your adapter assembly
2. The source generator runs during the consumer's build
3. The generator finds your attributes in the referenced assemblies
4. Your service definitions get included in the consumer's manifest

### For Package Authors
Make sure to:
- Include `Koan.Orchestration.Abstractions` as a dependency
- Add attributes to your adapter classes (not the assembly)
- Test your package in a consuming application

### For Package Consumers
The process is transparent:
1. Install NuGet packages with Koan adapters
2. Build your project (generator runs automatically)
3. Run `Koan up` (your dependencies are discovered automatically)

## Troubleshooting

### "No services discovered"
**Cause**: The generator didn't find any orchestration attributes, or the manifest wasn't embedded correctly.

**Solutions**:
- Make sure your adapter classes are public
- Verify you have `[ServiceId("...")]` on your classes
- Check that your project built successfully
- Look for `__KoanOrchestrationManifest.g.cs` in your `obj/` folder

### "Service not appearing in compose output"
**Cause**: The service was discovered but filtered out during planning.

**Solutions**:
- Ensure the `ServiceId` is unique
- Check that `ContainerDefaults` has a valid image
- Verify your adapter class is actually being referenced by your application

### "Health check failing"
**Cause**: The health endpoint isn't responding correctly.

**Solutions**:
- Make sure the health path returns HTTP 200 when ready
- Verify the endpoint is accessible from Docker containers
- Try increasing the timeout or interval values
- Remove the health check attribute if your service doesn't support HTTP health checks

### "Environment variables not set"
**Cause**: The token replacement isn't working correctly.

**Solutions**:
- Check your `AppEnvDefaults` syntax (should be `KEY={token}` format)
- Verify the tokens match the endpoint configuration
- Make sure you're using the right endpoint mode for your deployment

## Need Help?

If you're having trouble with the orchestration system:
1. Check the generated manifest in `obj/Debug/net*/generated/`
2. Run `Koan export` to see the generated compose file
3. Look at existing adapter implementations in the Koan codebase for patterns
4. File an issue with your service configuration and error output

## Technical Reference

For developers who need the technical details:

### Discovery Precedence
The CLI follows this precedence when building deployment plans:
1. **Descriptor File**: Explicitly provided orchestration configuration
2. **Generated Manifest**: Build-time generated `__KoanOrchestrationManifest.Json`
3. **Reflection**: Runtime discovery via legacy `OrchestrationServiceManifestAttribute`
4. **Demo Fallback**: Hardcoded services for development scenarios

### Generated Manifest Structure
The generator produces JSON with this structure:
```json
{
  "services": [
    {
      "id": "postgresql",
      "image": "postgres:16",
      "ports": [5432],
      "env": {
        "POSTGRES_DB": "myapp",
        "POSTGRES_USER": "user"
      },
      "volumes": ["/var/lib/postgresql/data"],
      "scheme": "tcp",
      "host": "postgres",
      "localScheme": "tcp",
      "localHost": "localhost",
      "localPort": 5432,
      "appEnv": {
        "DATABASE_URL": "postgres://{user}:{password}@{host}:{port}/{database}"
      },
      "healthPath": "/health",
      "healthInterval": 30,
      "healthTimeout": 10,
      "healthRetries": 3
    }
  ]
}
```

### Auto-Attachment
In the Koan repository, the generator is auto-attached to all projects via `Directory.Build.props` as an Analyzer reference, so you don't need to add a package reference explicitly.

### Error Handling
- Missing `ServiceId` or `ContainerDefaults.Image` → service candidate ignored
- Invalid attribute values → generator skips that piece and continues
- Generator failures are swallowed to not break builds
- Health checks only work for HTTP/HTTPS endpoints

### Attribute Reference

#### ServiceIdAttribute(string id)
- **Required**. Stable service identifier (e.g., "mongo", "postgresql").

#### ContainerDefaultsAttribute(string image, ...)
- **Required**. Container image name (e.g., "postgres").
- Named properties: `Tag` (string), `Ports` (int[]), `Env` (string[]), `Volumes` (string[])

#### EndpointDefaultsAttribute(EndpointMode mode, string scheme, string host, int port, ...)
- **Recommended**. Provide at least one for `EndpointMode.Container`. Optionally another for `EndpointMode.Local`.
- For HTTP/HTTPS services, set `scheme` to "http" or "https"
- Named properties: `UriPattern` (string)

#### AppEnvDefaultsAttribute(params string[] keyEqualsValue)
- **Optional**. Application-facing environment variables with token substitution
- Format: `"KEY={token}"` where tokens get replaced with actual values

#### HealthEndpointDefaultsAttribute(string httpPath, ...)
- **Optional** (HTTP-only). Enables compose healthcheck via curl when endpoint scheme is http/https
- Named properties: `IntervalSeconds` (int), `TimeoutSeconds` (int), `Retries` (int)
- Use lightweight paths like "/health" or "/v1/.well-known/ready"

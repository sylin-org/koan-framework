title: Koan.Data.Milvus - Technical Reference
description: Milvus adapter for Koan vector data.
packages: [Sylin.Koan.Data.Milvus]
source: src/Koan.Data.Milvus/

## Summary
- REST adapter that provisions Milvus collections and executes CRUD/search operations
- Supports JSON metadata filters translated to Milvus boolean expressions
- Provides health contributor and orchestration-aware configuration

## Capabilities
- VectorEnsureCreated: creates collection and fields if missing
- Upsert/UpsertMany: `/v2/vectors/upsert`
- Delete/DeleteMany: `/v2/vectors/delete`
- Search: `/v2/vectors/search` with expression filters
- Instructions: `data.ensureCreated`, `data.clear`
- Health: `/v2/health`

## Configuration Keys
- `Koan:Data:Milvus:Endpoint`
- `Koan:Data:Milvus:Database`
- `Koan:Data:Milvus:Collection`
- `Koan:Data:Milvus:Dimension`
- `Koan:Data:Milvus:PrimaryField`
- `Koan:Data:Milvus:VectorField`
- `Koan:Data:Milvus:MetadataField`
- `Koan:Data:Milvus:Metric`
- `Koan:Data:Milvus:AutoCreate`
- `Koan:Data:Milvus:Token` / `Username` / `Password`

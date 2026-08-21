# AotRelational — one app, five relational backends, published as a native binary

The whole point of this sample is that [Program.cs](Program.cs) does not change. It writes one `Note`
and reads it back through the ordinary `Entity<T>` surface. Which store answers is decided by the
connector referenced at build time and a connection string — never by the application code.

It exists to keep a specific claim honest: that a Koan application publishes under **NativeAOT** —
a single native executable with no installed .NET runtime — against a *server* database and not
only against the embedded floor. That claim is measured, not assumed; see
[ARCH-0093](../../../docs/decisions/ARCH-0093-nativeaot-substrate.md).

## Run it on the JIT

```bash
dotnet run --project samples/fundamentals/AotRelational        # SQLite, no server needed
```

## Publish it native

Windows publishes inside the VC developer environment; see
[the NativeAOT guide](../../../docs/guides/nativeaot-howto.md) for the toolchain and the reasons.

```cmd
call "...\VC\Auxiliary\Build\vcvars64.bat"
dotnet publish samples\fundamentals\AotRelational\AotRelational.csproj ^
  -c Release -r win-x64 -p:KoanAot=true -p:Connector=Postgres ^
  -p:IlcUseEnvironmentalTools=true -o artifacts\aot\Postgres
```

```bash
# Linux — clang is the linker
dotnet publish samples/fundamentals/AotRelational/AotRelational.csproj \
  -c Release -r linux-x64 -p:KoanAot=true -p:Connector=Postgres -o artifacts/aot/Postgres
```

`-p:Connector=` selects the backend: `Sqlite` (default), `Postgres`, `Cockroach`, `MySql`, `SqlServer`.

## Point it at a store

Each connector reads its own configuration section, so the environment variable differs by backend:

| Connector | Environment variable | Example value |
|---|---|---|
| `Sqlite` | *(none — writes beside the binary)* | |
| `Postgres` | `Koan__Data__Postgres__ConnectionString` | `Host=localhost;Port=5432;Username=koan;Password=…;Database=koanaot` |
| `Cockroach` | `Koan__Data__Cockroach__ConnectionString` | `Host=localhost;Port=26257;Username=root;Database=koanaot;SSL Mode=Disable` |
| `MySql` | `Koan__Data__MySql__ConnectionString` | `Server=localhost;Port=3306;Database=koanaot;User Id=root;Password=…` |
| `SqlServer` | `Koan__Data__SqlServer__ConnectionString` | `Server=localhost,1433;Database=koanaot;User Id=sa;Password=…;TrustServerCertificate=True` |

The binary prints the adapter that actually took the call (`adapter=NpgsqlRepository\`2`) before it
writes, so a silent fallback to another provider cannot be mistaken for a passing proof. It exits
non-zero and names the exception on any failure.

## Why the SQL Server build is different

`Microsoft.Data.SqlClient` refuses to open a connection in globalization-invariant mode —
`NotSupportedException: Globalization Invariant Mode is not supported`, thrown from `SqlConnection.TryOpen`.
That is the driver's own refusal, not an AOT limitation.

The project therefore sets `InvariantGlobalization=false` for `-p:Connector=SqlServer` and leaves it
`true` for the other four, which do not need culture data and publish smaller without it. Nothing extra
is required on the command line; the difference is recorded in the project file so the publish command
stays the same for every backend.

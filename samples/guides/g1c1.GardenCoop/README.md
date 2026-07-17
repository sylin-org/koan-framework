# Garden Cooperative

GardenCoop receives garden sensor readings, binds them to plots, and keeps one watering reminder
active while recent soil humidity is dry. It is a local, durable web application: SQLite, REST,
OpenAPI, admin UI, test authentication, startup facts, and the dashboard arrive through referenced
Koan capabilities.

## Run the complete story

From the repository root:

```pwsh
Set-Location samples/guides/g1c1.GardenCoop
dotnet run -- --urls http://localhost:5000
```

Open <http://localhost:5000>. A fresh database starts with three plots and three readings. Bed 3 is
dry, so the write lifecycle creates one active reminder and the console reports the simulated email.

The same result is inspectable without the dashboard:

```pwsh
Invoke-RestMethod http://localhost:5000/api/garden/plots
Invoke-RestMethod http://localhost:5000/api/garden/reminders
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

Stop the application with Ctrl+C. No external service or configuration is required.

## Why the code stays small

`Program.cs` is the complete host:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The application contains only its meaningful responsibilities:

- `Entity<T>` models describe members, plots, sensors, readings, and reminders.
- `EntityController<T>` controllers expose their standard HTTP capabilities.
- `GardenAutomation` declares sensor binding and watering rules at the Entity lifecycle boundary.
- `GardenCoopModule : KoanModule` composes those rules, seeds the first useful state, and explains the
  application in Koan's startup report.

Adding or removing a Koan project/package reference changes the available infrastructure capability;
the business host does not need parallel provider wiring.

## NativeAOT

GardenCoop is also a measured NativeAOT sample. Opt in through Koan's local project gate so the AOT
property does not flow into source-generator projects:

```pwsh
dotnet publish samples/guides/g1c1.GardenCoop/g1c1.GardenCoop.csproj `
  -c Release -r win-x64 --self-contained true -p:KoanAot=true
```

Run the executable from its publish directory so standard ASP.NET content-root discovery finds the dashboard:

```pwsh
Set-Location samples/guides/g1c1.GardenCoop/bin/Release/net10.0/win-x64/publish
./g1c1.GardenCoop.exe
```

The publish directory is a self-contained native deployment containing the executable, the static
dashboard, and SQLite's native library; it is not described as a single-file deployment.

The same project can target another supported RID when its NativeAOT toolchain is installed. See the
[NativeAOT guide](../../../docs/guides/nativeaot-howto.md) for platform prerequisites and the current
analysis-warning boundary.

# Sylin.Koan.Templates

`dotnet new` templates for the [Koan Framework](https://github.com/sylin-org/Koan-framework) — go from zero to a running app in one command.

## Install

```powershell
dotnet new install Sylin.Koan.Templates
```

## Templates

| Short name | What you get |
|---|---|
| `koan-web` | A minimal web API: the canonical 4-line `Program.cs`, one `Entity<T>` + one `EntityController`, Sqlite, full REST auto-mapped. |
| `koan-console` | A minimal console app: `StartKoan()` + one `Entity<T>` with `Save` / `Get` / `Query` over Sqlite. |

## Use

```powershell
dotnet new koan-web -o MyApi
cd MyApi
dotnet run
# -> GET http://localhost:5000/api/todos
```

The generated project references the `Sylin.Koan.*` meta-packages (`Sylin.Koan.App` for web, `Sylin.Koan` for console) — adding a package reference is the only "wiring"; the framework auto-registers everything (Reference = Intent).

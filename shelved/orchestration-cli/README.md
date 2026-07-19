# Shelved Koan orchestration CLI

This tree preserves the bespoke Koan development-host CLI experiment outside the Koan V1 product and package graph.
It is intentionally absent from `Koan.sln` and from the release compiler's active `src/`, `packaging/`, and top-level
`templates/` discovery roots.

The source remains buildable for later reassessment:

```powershell
dotnet build shelved/orchestration-cli/Koan.Orchestration.Cli/Koan.Orchestration.Cli.csproj -c Release
```

Do not treat source presence as a supported package promise. Moving this family back into active scope requires a
fresh user-value contract, comparison with standard Aspire/container tooling, repaired current application discovery,
focused owner/consumer evidence, and an explicit V1-or-later package decision.

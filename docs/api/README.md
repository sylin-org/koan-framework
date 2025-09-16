# Koan API Docs

This folder contains generated API reference content from XML documentation comments.

## Generate with DocFX

1. Install DocFX (once):
   - choco: `choco install docfx -y`
   - winget: `winget install docfx.docfx`
2. Build:
   - `docfx docs/api/docfx.json`
3. Serve locally:
   - `docfx docs/api/docfx.json --serve`

> The reference content is generated from the solution projects with XML docs enabled via `Directory.Build.props`.

# Solution Filters and Configurations

Open focused subsets of the solution with .slnf files:

- Sora.CoreOnly.slnf – Core + Data abstractions + tests
- Sora.DataOnly.slnf – All data libs + tests
- Sora.WebOnly.slnf – Web libs + GraphQL + samples + tests
- Sora.Full.slnf – Entire solution (default)

In VS Code: File > Open... and select the .slnf. In Visual Studio: File > Open > Project/Solution and pick the .slnf.

Configurations (use from CLI with -c):

- Debug (default)
- Release

Custom configs like Dev/Integration/CI can be added later if needed; current Debug build includes unit/integration tests but they skip when dependent services aren’t available.

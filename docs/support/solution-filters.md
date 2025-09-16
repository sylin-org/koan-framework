# Solution Filters and Configurations

Open focused subsets of the solution with .slnf files:

- Koan.CoreOnly.slnf – Core + Data abstractions + tests
- Koan.DataOnly.slnf – All data libs + tests
- Koan.WebOnly.slnf – Web libs + GraphQL + samples + tests
- Koan.Full.slnf – Entire solution (default)

In VS Code: File > Open... and select the .slnf. In Visual Studio: File > Open > Project/Solution and pick the .slnf.

Configurations (use from CLI with -c):

- Debug (default)
- Release

Custom configs like Dev/Integration/CI can be added later if needed; current Debug build includes unit/integration tests but they skip when dependent services aren’t available.

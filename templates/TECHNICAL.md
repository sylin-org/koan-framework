# Sylin.Koan.Templates technical contract

The template pack is a content-only standard NuGet `Template` package. Its three suppressed
ProjectReferences express release impact for the two bundles and SQLite connector used by generated
projects; they must not appear as dependencies in the template nupkg.

Template source contains package-range tokens rather than version judgment. `Koan.Packaging` resolves
each floor from the selected release manifest or the latest public stable package, compiles Koan's
closed-open compatibility band, copies only source content to a temporary root, and supplies that root
to `dotnet pack`. Direct packing fails before emitting a package because it cannot prove those release
facts.

The packed artifact must contain both `.template.config/template.json` files at their canonical paths,
contain no `bin`, `obj`, unresolved token, or NuGet dependency, and generate projects whose only setup
is ordinary PackageReference restore.

The release clean room installs the exact template nupkg into an isolated `DOTNET_CLI_HOME`, creates
both projects using their public short names, discovers the project name chosen by `dotnet new`, and
restores/builds/runs only against the staged package feed. Console proof requires visible Entity
save/load/query results; web proof requires a healthy host and a persisted Todo through the generated
`EntityController<Todo>`. A failure blocks the package wave before publication.

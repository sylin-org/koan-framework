# Sylin.Koan.App technical contract

`Sylin.Koan.App` is a dependency-only composition package. Its NBGV path inputs include the foundation
bundle and the web runtime, so a changed tested composition mints a new App version without borrowing
the version of Core or any other component.

Each dependency receives its own bounded range at pack time. The package contains no runtime assembly
and intentionally produces no symbol package.

# Sylin.Koan technical contract

`Sylin.Koan` is a dependency-only bundle. Its independent NBGV version changes whenever its own
manifest or one of its composed package inputs changes. Packing evaluates every `ProjectReference`
and emits that component's actual bounded compatibility range through `build/compat-ranges.targets`.

The bundle contains no runtime assembly and intentionally produces no symbol package.

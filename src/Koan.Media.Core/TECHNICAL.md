# Koan.Media.Core — technical contract

## Startup

`Koan.Media.Core.Initialization.KoanAutoRegistrar` is a `KoanModule`. Registration binds the
`Koan:Media` options root and installs one `IMediaRecipeRegistry`. `Start()` resolves and materializes the
catalog so recipe binding errors fail host startup. `MediaCompositionContributor` separately reports the
valid catalog through the fail-soft runtime-facts seam.

## Recipe resolution

`MediaRecipeRegistry` is the single decision:

1. discover parameterless static methods marked `[MediaRecipe]`;
2. bind `Koan:Media:Recipes` declarations;
3. let configuration replace code on a name collision;
4. reject reserved format-shortcut names; and
5. synthesize only format shortcuts the encoder registry can produce.

The registry supports configuration reload through `IOptionsMonitor`. A reload with invalid configuration
throws from the change callback; it does not roll forward a partially rebuilt catalog.

## Pipeline posture

The pipeline is lazy until `ProbeAsync`, `WriteToAsync`, `ToBytesAsync`, or `MaterializeAsync`. Prefer
`WriteToAsync` for output; `ToBytesAsync` is a buffered compatibility terminal. Source limits supplied by Web
are checked before full decode, while direct Core callers own their ingress bounds.

## Inspection

Runtime facts report recipe count, producible shortcuts, and each recipe's source, version, fingerprint,
step count, mutators, and format posture. The HTTP JSON projection is owned by Web and reads this same registry.

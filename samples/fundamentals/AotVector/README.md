# AotVector

The vector-plane twin of [AotRelational](../AotRelational/README.md): one save, one read, one
search, and one delete through the ordinary `Vector<T>` surface, published as a **single NativeAOT
binary** and run against a real Chroma server. It exists because the vector claim has the same
shape as the relational one — ILC forbids things the JIT allows, so only a publish-**and-run**
proves it — and because every Koan vector adapter is HTTP, meaning there is no native driver to
credit or blame: the wire path itself must survive ILC.

The binary also references the llama.cpp AI connector to prove the AI seam composes and boots under
ILC; with no endpoint configured it stays inactive, which is the correct posture, not a failure.

## Run (JIT)

```powershell
docker run -d --name koan-chroma-aot -p 8000:8000 chromadb/chroma:1.5.9
pwsh ./start.bat
```

Expect `adapter=ChromaVectorAdapterFactory` and `OK`. Stop the container when done:

```powershell
docker rm -f koan-chroma-aot
```

## Run (NativeAOT)

```powershell
docker run -d --name koan-chroma-aot -p 8000:8000 chromadb/chroma:1.5.9
dotnet publish -c Release -r win-x64 -p:KoanAot=true
./bin/Release/net10.0/win-x64/publish/AotVector.exe
```

Expect the same receipt and `OK` from the single-file binary. Publish success is not the claim —
the run is.

## Notes

- The endpoint comes from configuration (`Koan:Data:Chroma:Endpoint`, e.g. env
  `Koan__Data__Chroma__Endpoint`); the default is `http://localhost:8000`.
- Vector metadata crosses as a **dictionary**: under ILC, anonymous-object properties are trimmed,
  so `new { X = 1 }` metadata that works in JIT loses its members in AOT.
- A scratch NativeAOT probe **outside this repository** must import
  `src/Koan.Core/build/Sylin.Koan.Core.targets` itself — in-repo projects receive it centrally from
  `Directory.Build.targets`. Without it the module manifest and trim roots are missing and boot
  discovers no adapters.
- Machine-checked by `scripts/aot-verify.ps1` is the *relational* matrix; this sample is the
  vector-plane evidence, verified the same way (publish **and run**, adapter receipt required).

**Working with a coding agent?** [AGENTS.md](../../../AGENTS.md) at the repository root orients any agent on the Koan conventions this sample follows.

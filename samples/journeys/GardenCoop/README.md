# GardenCoop — grow one useful application

GardenCoop is Koan's capability-addition journey. It starts with a real cooperative need and grows by
adding one business-visible capability at a time. It is not a sequence of disconnected feature demos.

| Chapter | Preserved result | New result | Added capability |
|---|---|---|---|
| [01 — Garden Journal](01-GardenJournal/README.md) | — | A dry reading creates one watering reminder; recovery acknowledges it | Entity lifecycle, SQLite, Web, facts |
| [02 — Local Discovery](02-LocalDiscovery/README.md) | Complete garden journal | Natural-language produce discovery, entirely local | Entity embeddings, ONNX, sqlite-vec |

Each chapter is independently runnable and testable. A later chapter graduates only when its cumulative
contract proves every earlier business result before proving the new one. That makes capability growth
visible as meaningful, small steps: references add mechanics; application code adds business intent.

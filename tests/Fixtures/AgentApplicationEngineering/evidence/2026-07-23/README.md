# Cold-agent evaluation — 2026-07-23

Each prompt in `../../evaluations.json` ran once in an independent cold coding-agent context. The
agent was told to read the public guide, follow public links only as needed, make no repository
changes, and answer the prompt as a user-facing implementation response.

The raw responses are retained beside `scores.json`. Scoring uses only the fixture's declared
signals; it does not add a model matrix or repeat successful prompts.

All three prompts passed.

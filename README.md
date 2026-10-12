# ENORYS · Cognitive Sandbox

**Experimental cognitive architecture for autonomous agents.** A small but runnable browser sandbox illustrating how partial observations, remembered events, social reports and explainable utility-based decisions can produce different histories from the same initial agents.

> **Status: interactive demo / early research.** This is **not** the full NOEMYN v0.1.1 specification, a C# SDK, or a validated model of psychology. The planned C# CPU-first implementation remains future work.

## Try the demo

Open [`index.html`](./index.html) through a local HTTP server, or enable GitHub Pages in **Settings → Pages → Deploy from a branch → main → /(root)** to make it available in the browser. No external services, build tools, API keys or network requests are required at runtime.

```bash
# In a checkout of this repository:
python3 -m http.server 8000
# Open http://localhost:8000
```

Alternatively use Node.js 20+ to run the automated tests:

```bash
npm ci
npm test
```

### What you can do

- Create **6–60 agents** with seeded individual traits, resources and zones.
- Advance **one or 100 decisions** at a time; watch taking, giving, waiting and passing reports.
- Inspect every candidate action's **utility components**.
- Select an agent and inspect **eyewitness vs hearsay provenance** and changing relationships.
- Run a **counterfactual comparison** with the same seed and agents but 0% vs 100% witness chance.
- File issues with your seed, agent count, witness chance and steps so others can reproduce behavior.

## Experiment 004 — Agent Evolution Lab (v0.4)

Four sequentially tested reference architecture steps: **survival domain boundaries**, **multi-step goals and dynamic reservations**, **private evidence / social obligations**, and **conservative Cognitive LOD**. This is runnable **JavaScript reference code**, not the finalized .NET kernel.

- [Interactive Agent Evolution Lab](evolution.html) — compare same-seed scenarios, inspect NPC memories and event histories.
- [Methodology, expectations vs outcomes](docs/EXPERIMENT-004.md) — 40 paired seeds, 100 fuzz seeds, 100 LOD audit seeds, 50/100/500-NPC indicative benchmarks.
- [Architecture and next release gates](docs/ARCHITECTURE-V04.md).

```bash
node --test tests/v04-*.test.js
node scripts/experiment-004.mjs
python3 -m http.server 8000   # open http://localhost:8000/evolution.html
```

**Limitations:** old v0.3 files remain reproducible and unchanged. In v0.4, 500-agent LOD speedups are **not** yet substantiated; the .NET/Godot integration remains outstanding.

## Modular extensibility — proposed production contract

ENORYS is designed to remain **game-agnostic**. Users should be able to add their own needs, items, emotions, norms and actions as validated namespaced **Data Packs**, add decision/appraisal/psychology algorithms as versioned **Behavior Modules**, and connect Godot or other simulations through independent **World Adapters**, without editing or rebuilding the Core. The Affective Cognitive Map is replaceable, not mandatory.

Read [ADR-0003 — extensible modules and user-authored data](docs/ADR-0003-EXTENSIBLE-MODULES.md) and [ADR-0002 — Affective Cognitive Map](docs/ADR-0002-AFFECTIVE-COGNITIVE-MAP.md). **Status: proposed architecture**. The plugin loader, data schemas and public module SDK are **not implemented yet**. Their acceptance tests are specified in ADR-0003.

## What the prototype does NOT do

No natural language generation, LLM integration, lying, planner, persistence, learning, Godot adapter, validated emotions, or high-fidelity cognition. Rules and trait weights are **toy experimental defaults**, not psychologically calibrated. An audit log is available to the UI but never passed into an agent's decision function.

## Architecture

- `src/engine.js`: seeded RNG, isolated agent observations, memory, relationships, decisions, authoritative world adapter, deterministic event log.
- `web/app.js`, `web/style.css`, `index.html`: dependency-free interactive UI.
- `tests/engine.test.js`: determinism, provenance, utility transparency, conservation of resources, validation.
- `docs/ARCHITECTURE.md`: boundaries, assumptions and known limitations.

## Development and contribution

- `main`: tested public version.
- `develop`: integration branch.
- [Open a bug report or feature request](https://github.com/Pivtorak-a/ENORYS/issues). Please include reproduction inputs.
- If you'd like to contribute code, open an issue first to discuss scope.

## License

**No license chosen yet.** Code is public for inspection; public visibility by itself does not confer broad rights to copy, modify or distribute it. Maintainer will choose a code license before calling the project open source.

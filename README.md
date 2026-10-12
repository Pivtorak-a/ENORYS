# ENORYS · Modular Cognitive Engine

**Headless, modular, data-driven cognitive agent research engine, targeting C# / .NET 10.** Current experimental v0.6 includes a runnable C# world/agent loop, survival domain, plan persistence, socially transmitted evidence, and a validated external C# plugin and data-pack example.

> **Status:** C# v0.6 engineering proof of concept, **not** a production SDK, complete Affective Cognitive Map, Godot game or validated psychological simulator. The existing JavaScript tools are independent reference demos.

### Run the C# engine

Requires the **.NET 10 SDK**. From the repository root:

```bash
dotnet run --project tests/Enorys.Integration/Enorys.Integration.csproj -c Release -- test
dotnet run --project tests/Enorys.Integration/Enorys.Integration.csproj -c Release -- bench
# Print a reproducible NPC event story as JSON:
dotnet run --project tests/Enorys.Integration/Enorys.Integration.csproj -c Release -- simulate 42 24 14
```

**Source:** [Enorys.Abstractions](src/Enorys.Abstractions) · [Enorys.Core](src/Enorys.Core) · [Enorys.Domains.Survival](src/Enorys.Domains.Survival) · [External C# plugin](examples/Enorys.CustomBehavior) · [JSON Data Packs](data-packs/).

**Historical v0.5 measurement:** 500 NPCs × 14 days × 3 turns/day = 21,000 decisions in ~303 ms median on a GitHub x64 Linux .NET 10 runner; process peak ~98 MB, short-lived allocation ~12.45 KB/turn. Read the [benchmark report](docs/NET-MIGRATION-BENCHMARK.md) for workload, CI data, GC counts and limitations. These values are **not** Godot FPS.

## v0.6 — Wake Scheduler and modular emotional cognition (experimental)

The C# engine now supports:
- **Cached immutable world and actor-private views** to reduce temporary allocation pressure;
- **Hybrid event-aware wake scheduling** with domain-defined sleep policies and message-triggered early wakeups (opt-in; may change simulated decisions);
- Optional **Affective Cognitive Map** (`src/Enorys.Affect.ACM/`): source-linked subjective anger, fear, sadness, gratitude and guilt, lazy decay, retractions and bounded influence on decisions;
- **Custom emotions and triggers entirely from JSON** (`data-packs/affect-custom.json`), without changing Core;
- End-to-end tests for mixed emotions, evidence anti-echo, private data isolation, accurate material ledger and scheduler replay.

Run the additional benchmark suites:

```bash
dotnet run --project tests/Enorys.Integration -c Release -- test
dotnet run --project tests/Enorys.Integration -c Release -- bench-scheduler
dotnet run --project tests/Enorys.Integration -c Release -- bench-acm
```

**Observed 500-NPC batch allocation:** about **10.36 KB / nominal actor turn** with ACM off and scheduler off, down from ~12.45 KB in v0.5. This is still above the provisional <3 KB/turn optimization goal. Full methodology, timing variability, and ACM costs: [Experiment 005 — .NET Scheduler and ACM](docs/EXPERIMENT-005-NET-SCHEDULER-ACM.md).

**Scope:** first optional functional emotional module and hybrid wake scheduling, **not** a full validated emotional cognition architecture, a no-scan discrete-event scheduler or a Godot frame-rate test.

## Native C calculation experiment (optional)

An independent native C library now benchmarks exact Noisy-OR causal inference against the existing C# math and a no-allocation C# implementation. **It is an experiment, not a mandatory ENORYS runtime dependency.**

On Linux x64 (GCC `-O3`, .NET 10), 40,000 two-cause evaluations ran in **15.45 ms** with original C#, **6.66 ms** in optimized C#, and **4.87 ms** via native C/P-Invoke. Larger eight-cause evaluations showed smaller native gains. [Read the complete benchmark, numeric verification and caveats](docs/EXPERIMENT-006-C-VS-CSHARP.md).

The C source is in [native/](native/), and the runnable comparison is in [benchmarks/Enorys.NativeMath/](benchmarks/Enorys.NativeMath/). Windows and macOS binary compatibility and speed are unverified.

## C versus Zig versus Go: live NPC benchmarks

[Experiment 007 — measured world-profile and language comparison](docs/EXPERIMENT-007-C-ZIG-GO-PROFILE.md) adds optional C/Zig/Go implementations of a single hot-path numeric choice operation, and compares them with C# over reproducible 24, 100 and 500-NPC worlds. All backends reproduce identical agent decision traces in the tested scenarios.

**Result:** 500-NPC world: C# direct for loop ~185.4 ms, C# managed span **~181.9 ms**, C **~183.4 ms**, Zig **~182.1 ms**, Go **~189.6 ms** (median of 5 runs, shared Ubuntu runner). Only **~0.48%** of world time is the numeric argmax itself in a profiled run. Per-call native FFI is much slower for tiny vectors. This is **not** evidence for rewriting the whole engine in a native language. The default C# path now removes one per-decision LINQ iterator without any required native dependencies.

Native builds and verification via [C/Zig/Go CI workflow](.github/workflows/c-zig-go-world.yml). The existing C# engine still requires none of these optional libraries.

## Experimental PulseGraph architecture — sparse, causal event scheduling

[PulseGraph ADR-0004](docs/ADR-0004-PULSEGRAPH.md) explores a more unusual **event-driven cognition schedule**: don't scan every NPC's needs every tick. Instead, compute each signal's next relevant threshold crossing and wake an agent only when a deadline or a private observation arrives.

The **standalone C# experiment** in [experiments/Enorys.PulseGraph](experiments/Enorys.PulseGraph) compared an exact sparse scheduler against a reference tick scan. In **54 matched seeded test cases**, action counts and ordered action-trace checksums agreed. On a GitHub CI runner, 5,000 NPC and nine signals took **192.60 ms by full scanning vs 29.31 ms by PulseGraph (6.57×)**. But a dense-observation case was **slower with PulseGraph** (53.07 vs 28.24 ms for 500 NPC), and the event queue allocated more memory.

This is **NOT a drop-in replacement for ENORYS Core**: the proof tests deterministic threshold regulation, *not* the complete world, social cognition, ACM, or Godot. A future production architecture should combine sparse distant agents with dense ECS updates for active/high-interaction regions.

Read the [complete benchmark protocol, negative results, modularity and integration gates](docs/EXPERIMENT-008-PULSEGRAPH-BENCHMARK.md). Run with `dotnet run --project experiments/Enorys.PulseGraph -c Release -- test` and `-- bench`.

## Legacy research demos (JavaScript)


**Experimental cognitive architecture for autonomous agents.** A small but runnable browser sandbox illustrating how partial observations, remembered events, social reports and explainable utility-based decisions can produce different histories from the same initial agents.

> **Legacy demo status:** the browser sandbox remains separate from the C# kernel; its scenario-specific algorithms are not the general-purpose plugin API.

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

This **legacy browser demo** has no natural language generation, LLM integration, persistence, or Godot adapter; it is not a high-fidelity cognitive simulator. The newer C# engine **does** include an experimental planner and memories, but not the proposed full Affective Cognitive Map. Rules and trait weights are **toy experimental defaults**, not psychologically calibrated. An audit log is available to the UI but never passed into an agent's decision function.

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

# ENORYS experiment 005 — C# allocation caches, wake scheduling and modular ACM

**Date:** 2026-10-12  
**Release target:** experimental v0.6  
**Scope:** C# / .NET 10 headless engine; browser JavaScript demos stay separate.

## Changes verified

### 1. Allocation optimizations
- Immutable **agent-private world snapshots** cached until resource/need changes (read-only dictionary copies retained by prior readers).
- Zone peer lists reused while zone membership stays unchanged; rebuilt when an agent dies.
- Public depot and known-source views invalidated when authoritative supplies change; no stale packets or world mutation granted to cognitive modules.
- Agent's private episodic evidence, trust, debt and request views cached as immutable/frozen snapshots until social state changes.
- Stable actor IDs and typed domain contracts retained.

### 2. Hybrid event-aware wake scheduler
- `WakeScheduler` tracks next due decision by actor slot; the trusted domain-specific `IWakePolicy` chooses an optional number of skipped turns.
- `IObservationSignal.HasPendingObservation` wakes an NPC early when the adapter delivers a private message **without consuming that message**.
- Existing full-deliberation mode is the default; `EngineOptions(EventScheduling:true)` activates the policy.
- The world continues to advance objective time and physiological needs on scheduled slots even if cognition is skipped.
- Sleeping/waiting may skip a turn; **this may change outcomes**. This is an approximate cognition budget policy, not semantic equivalence to full deliberation and not a full discrete-event physics engine.
- Determinism, material invariants and at-least-once wake handling are covered by integration tests.

### 3. Replaceable Affective Cognitive Map (ACM)
- New `Enorys.Affect.ACM` .NET project uses only `Enorys.Abstractions`, not the survival adapter or Core.
- Optional `IAffectiveModule` registered through `IModuleRegistry`. If absent, behavior remains functional.
- The sample catalog `data-packs/affect-basic.json` defines five working emotional types (anger, fear, gratitude, sadness, guilt), rates of time decay, and action-utility biases.
- Additional emotions can be created without code modification via `data-packs/affect-custom.json`: e.g., `customaffect:relief` triggered by retraction and `customaffect:disappointment` triggered by an accusation.
- Each episode carries agent, root source, emotion and target; the exact source root cannot be independently amplified by repeated hearsay.
- A retraction weakens emotion associated with that source but **does not instantly erase** unrelated emotional memories.
- Mixed emotions toward one person (anger plus gratitude) can coexist.
- Per-agent private observations are the *only* input to affect; the World Authority never directly assigns emotions.
- In the first version, ACM contributes bounded scores to candidate decisions; Cognitive LOD conservatively falls back to full deliberation when any affect module is installed.

**ACM limits:** The sample baseline five feelings use experimental hand-coded appraisals inside the replaceable plugin, not a general causal emotional graph or psychologically validated equations. The generic content triggers support new catalog-defined emotions, but user-defined belief appraisal algorithms still require an external C# plugin. Episode retention is unbounded; save/load and a persistent emotion graph are not implemented. This is the first functional slice of ADR-0002, **not full ACM**.

## Exact verification scope

CI: [experiment 005 — validated .NET 10 integration, benchmarks](https://github.com/Pivtorak-a/ENORYS/actions/runs/38204852364).

- 24/50/100/500 NPC survival runs and material ledger checks.
- 12 matched full/LOD seeds; 40 randomized scarcity runs.
- Deterministic action-trace hashes.
- Scheduler sleeping, private-message early interruption and deterministic reproduction.
- Cached world views preserve the previous observation after state mutation (test added to the final suite).
- Source-root idempotence, subjective retraction, simultaneous conflicting feelings, monotone decay, no hidden-truth access and data-only custom emotion.
- Optional affect module installed/not installed; world resource conservation after affect-driven choices.
- Independent external decision DLL/data-pack demonstration from v0.5.

### Benchmarks — compare like-for-like as far as possible

All timings are **headless only**: no Godot, rendering, physics or multi-frame animation. Runner: GitHub Actions hosted Ubuntu 24.04.5, x64, .NET 10.0.12, four detected logical CPUs. Release build. Full-run 14-day experiments, three nominal actor turns per day.

#### New C# baseline (ACM off; scheduler off; median of five)

| NPC | Nominal turns | Full decision median | Temporary allocations / nominal turn | Peak working set |
|---:|---:|---:|---:|---:|
| 24 | 1,008 | 16.35 ms | 7.64 KB | 63.5 MB |
| 50 | 2,100 | 37.09 ms | 7.89 KB | 77.6 MB |
| 100 | 4,200 | 102.69 ms | 8.22 KB | 79.7 MB |
| 500 | 21,000 | **209.45 ms** | **10.36 KB** | **100.0 MB** |

For comparison, the previous v0.5 CI reported approximately **12.45 KB/turn** at 500 NPC. The change to **10.36 KB/turn** is **−16.8%** by these whole-run allocation metrics; this improvement is testable against a retained previous version and does not claim the provisional <3 KB/turn target has been met.

The previous v0.5 full 500-agent run measured 302.53 ms median, but **we cannot attribute the entire measured time difference to the code** because CI is shared hardware and memory management / workload changes may affect timing.

#### Event-aware scheduling (matched seeds and runs; median five)

| NPC | Scheduler off | Scheduler on | Mean skipped / nominal turns | Allocation off → on |
|---:|---:|---:|---:|---:|
| 24 | 17.27 ms | 16.21 ms | 55 / 1,008 | 7.60 → 7.25 KB/turn |
| 100 | 102.91 ms | 68.79 ms | 177 / 4,200 | 8.23 → 7.97 KB/turn |
| 500 | 221.44 ms | 202.51 ms | 621 / 21,000 | 10.38 → 10.35 KB/turn |

This is a **hybrid wake policy**, not a proof of automatic-vs-full decision equivalence. The majority of decisions still deliberate and performance differences on hosted CI vary. Outcomes may diverge when turns are skipped. More event-dense benchmarks are needed.

#### Optional ACM overhead (matched seeds; three repeats, full deliberation)

| NPC | ACM disabled | ACM enabled | Allocated / nominal turn off → on | Avg active episodes |
|---:|---:|---:|---:|---:|
| 24 | 19.08 ms | 34.08 ms | 7.63 → 8.00 KB | 68 |
| 100 | 78.18 ms | 143.54 ms | 8.16 → 8.60 KB | 667 |
| 500 | 375.85 ms | 338.47 ms | 10.35 → 10.99 KB | 12,178 |

**Interpretation:** ACM increases memory allocation and often CPU cost. The apparent 500-NPC ACM-on *speedup* is noise or complex workload variation, not evidence that adding emotions accelerates the runtime; do not generalize these CI results. Emotional storage and linear per-action bias scans become serious scaling concerns.

## Measured capacity and limits

The 500-NPC full headless reference completes 21,000 nominal actor turns in ~0.21 seconds median on this runner, ≈100k nominal turns/s as a batch average. This is sufficient for continued research with hundreds of NPCs at low update frequency, but is **not** a frame-time guarantee and **not** a full game benchmark.

At 500 NPC, estimated 10.36 KB/turn still implies ~311 MB/s temporary allocation if every NPC deliberated at 60 Hz (30,000 decisions/s), *before* rendering or physics; such a design is not advised. Do not translate a batch throughput number directly into fps.

## Open release gates

1. Target **<3 KB transient allocations/decision**, especially by replacing repeated LINQ candidate scans and per-step temporary lists, and profiling actual GC pauses on a dedicated machine.
2. Build a genuine wake-up **priority queue and event/need deadline scheduler** rather than iterating every NPC at each nominal slot. Preserve hidden knowledge and natural resource progression.
3. Bound and index ACM emotional episodes by target, action and root, with a retention policy and belief-driven reappraisal using actual semantic norms, not only hard-coded reaction coefficients.
4. Preserve save-state snapshots and module version migrations with consistent graph provenance and deterministic trace history.
5. Godot adapter and objective game-world sensing before any claims about 30/60 fps in SimWorld.

Run locally:

```bash
dotnet run -c Release --project tests/Enorys.Integration -- test
dotnet run -c Release --project tests/Enorys.Integration -- bench
dotnet run -c Release --project tests/Enorys.Integration -- bench-scheduler
dotnet run -c Release --project tests/Enorys.Integration -- bench-acm
```

No stable plugin SDK or full emotion architecture is claimed.

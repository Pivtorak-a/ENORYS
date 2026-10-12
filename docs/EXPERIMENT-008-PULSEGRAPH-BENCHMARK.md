# Experiment 008 — PulseGraph discrete-event cognition scheduling

**Date:** 2026-10-12  
**Status:** successful **standalone C# scheduling proof of concept**, not yet installed as the default ENORYS `Engine`.  
**CI:** [PulseGraph event-driven cognition — verified run](https://github.com/Pivtorak-a/ENORYS/actions/runs/38209973505) (compile, 54 parity cases, 5 repeated benchmarks).  
**Code:** `experiments/Enorys.PulseGraph/`  
**Architecture:** [ADR-0004 — PulseGraph](ADR-0004-PULSEGRAPH.md)

## Hypothesis

When most NPC needs change predictably and critical observations are sparse, computing next *threshold-crossing wakeups* is cheaper than scanning every need of every NPC on each logical tick **without changing action decisions**. At high event density, maintaining/cancelling a heap may cost *more* than an ordinary array scan.

The prototype uses a **typed, data-defined signal pack** with nine survival-like needs (thirst, hunger, fatigue, cold, safety, belonging, hygiene, morale, bladder). Optional external data pack adds `custom:radiation` and `custom:curiosity` without changes to the engine. Signals evolve as integer levels by constant tick rates. A regulatory action reduces its own signal and alters an adjacent signal; that coupling is included in both models.

An independently deterministic external stimulus stream is applied *before* per-tick decisions. The reference implementation scans all NPCs every tick. PulseGraph materializes an agent only when its earliest possible need threshold arrives, or an incoming stimulus invalidates its deadline. Tie-breaking is by earliest tick then agent ID; only one action per agent per tick, matching the reference.

## Correctness gates

**54 seeded comparison scenarios** passed, using:
- 9 and 11 namespaced registered signals;
- populations 24, 500 and 5,000;
- stimulus densities of 0, 0.3 or 5 per actor over the 1,440-tick window;
- seeds 17, 42 and 81.

Each case compares exact total action counts, action counts by registered signal, first twelve event summaries, and an ordered FNV-64 action-trace hash. No divergence observed. The hash is not collision-free proof of full transcript equality; a future production integration should support byte-for-byte stream comparison and snapshot replay.

The benchmarks also abort if the reference and sparse action traces differ in their checksums/counts, including a **flood of 150,000 observations**.

## Measured workload

- C# .NET 10 (Release), GitHub-hosted Ubuntu 24.04 x64, standard `PriorityQueue`.
- 1,440 integer clock ticks, nine registered signals unless otherwise specified; linear deterministic drift and local cross-signal action effects.
- Five repeats after one short JIT warmup; alternating run order to reduce first-run bias; all stimuli generated outside timed simulation.
- Reported **elapsed wall time median**, not CPU cores/threads, animation speed, psychological realism or a frame time.
- Managed allocation number is approximate **total allocated during one simulation**, not peak resident memory or the memory used to pre-generate external events.
- CPU-only, no physics/Godot, social reasoning, causal epistemics, full ACM graph, pathfinding or LLM. Cannot infer speedup of the v0.6 full engine.

## Expected and measured

| Agents | Signals | Exogenous observations | Actions (both) | Full tick scan | PulseGraph | Speedup | Allocations: scan → pulse |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 500 | 9 | 150 | 16,883 | 19.04 ms | 11.60 ms | **1.64×** | 0.023 → 0.052 MiB |
| 5,000 | 9 | 1,500 | 169,151 | 192.60 ms | 29.31 ms | **6.57×** | 0.194 → 0.547 MiB |
| 20,000 | 9 | 6,000 | 676,465 | 772.35 ms | 131.90 ms | **5.86×** | 0.767 → 2.052 MiB |
| 5,000 | 9 | 60,000 | 214,317 | 199.29 ms | 48.82 ms | **4.08×** | 0.194 → 1.592 MiB |
| **500** | 9 | **150,000** | **131,105** | **28.24 ms** | **53.07 ms** | **0.53× (slower)** | 0.023 → 1.903 MiB |
| 5,000 | **11** | 1,500 | 194,667 | 229.15 ms | 36.24 ms | **6.32×** | 0.233 → 0.585 MiB |

Exact CI values are rounded to two decimals here. The 500-agent dense case shows **PulseGraph runs 1.88× longer** than scanning (53.07/28.24); this is a genuine negative result and requires a hybrid selection policy.

### Work avoided

In the sparse nine-signal 5,000-NPC test:

```text
Full scan checks         = 64,800,000
PulseGraph signal checks = 3,097,836
Difference               = 61,702,164 (95.2% fewer)
```

This translates to only a **6.57× wall-time gain**, not 20.9×, due to priority queue operations, deadline recomputation and other work. The heap also requires extra allocations.

### Why high density loses

At 500 NPC and 150,000 external observations over 720,000 possible actor-tick slots, many earlier deadlines are invalidated and the heap needs frequent insertions/removals. A cache-friendly dense array loop becomes cheaper. The break-even point depends on agent count, registered signals, urgency dynamics, hardware and events; a universal numeric activation percentage has **not** been established.

## Proposed hybrid for ENORYS

```text
 World Authority (fixed physics tick)
    |
    +-- Sparse distant NPCs → PulseGraph wake deadlines
    |
    +-- Active combat/nearby NPCs → Dense ECS/SoA batch updates
    |
    +-- Agent-local memory/causal evidence graph → Event invalidations only
    |
    +-- Hard/uncertain situations → Bounded planner + ACM + optional LLM
```

The original `Engine` stays the fallback until integration is verified. A production `IActivationPolicy` may switch an agent/zone between sparse and dense modes at stable frame boundaries, with hysteresis to avoid thrashing. Rate estimators must not hide critical observations or change source provenance. A single authoritative world action commit remains compulsory.

### Technical priority

1. Introduce public plugin contracts for registered signals, dependency subscriptions and safe next-wakeup bounds.
2. Upgrade `SurvivalWorld` with exact need deadlines; test identical objective resources/health/decisions over realistic days under both schedulers.
3. Add actor-local evidence and emotion provenance invalidation, without granting hidden truth.
4. Support conservative fallback when an arbitrary module cannot predict its next wakeup.
5. Benchmark mixed active/dormant zones (e.g. 500 active + 5,000 sleeping) and high event bursts. Include peak RSS, GC pauses and real Godot frame time.
6. Replace general priority queue with bounded calendar/timing-wheel queues **only if** profiling supports it.
7. Keep modular JSON packs and external C# modules; native C/Zig remain optional dense arithmetic accelerators.

## References/relationship to prior research

Discrete-event simulation is well known; this experiment tests its particular application to ENORYS and its plugin architecture, **not a scientific claim of inventing event-based computation**. Planned active-inference deliberation over information gain is a separate high-level policy experiment, and should not be confused with the scheduling gains measured here.

## Reproduce

```bash
dotnet run --project experiments/Enorys.PulseGraph -c Release -- test
dotnet run --project experiments/Enorys.PulseGraph -c Release -- bench
```

The first validates trace parity; the second runs both algorithms on identical controlled data. No extra C, Zig, Go or JavaScript runtime is required.

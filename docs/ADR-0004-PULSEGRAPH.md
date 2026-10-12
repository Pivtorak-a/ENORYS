# ADR-0004 — PulseGraph: hybrid event-driven cognition

**Status:** Experimental proof in C#/.NET 10. **Not yet the primary ENORYS runtime.**
**Date:** 2026-10-12
**References:** [ADR-0001](ADR-0001-ARCHITECTURE.md), [ADR-0002](ADR-0002-AFFECTIVE-COGNITIVE-MAP.md), [ADR-0003](ADR-0003-EXTENSIBLE-MODULES.md).

## Decision being tested

Use a **causally invalidated, sparse and deadline-driven cognitive execution graph**, instead of continuously iterating over every NPC and every cognitive module.

A module may publish:
- a name, version, and typed, namespaced state/signal IDs (data-defined);
- immutable input dependencies and provenance requirements;
- a computation for a dirty node when its inputs change;
- an optional *conservative next-deadline function* (earliest future tick at which its value can change a choice), or an explicit `unknown` requiring periodic polling;
- an optional utility contribution and bounded result budget.

The authoritative world keeps ownership, collision, validation and physics. Each agent keeps private observations, causal beliefs, norms, memories and emotions. New input observations invalidate **only dependent agent-local nodes**, not all NPCs. Runtime selects activated NPCs by a calendar/priority queue; module-owned versions invalidate outdated wakeups.

## Computational model

```text
      Authoritative World                      DATA PACKS
     (truth & actions)                         signal schemas
             |                                      |
      filtered observation                   registered modules
             |                                      |
    +-- Agent-local Input Graph <-------------------+
    |    Need signals · Evidence · Relationships · Environment
    |                 |
    |      dependency invalidation
    |                 |
    |       earliest safe deadline
    +-------------> PulseScheduler
                         |
                3 compute budgets
           Fast reflex / Deliberation / LLM
                         |
                  ActionProposal
                         |
             World validates & commits
                         |
                new observations
```

### Three-level execution

1. **Hot / automatic:** deterministic low-cost responses, threshold-crossing needs and actions with proven unchanged choices. No deep memory scan.
2. **Warm / deliberate:** when candidate utilities are close, contradictions appear, confidence is low or planning fails; use indexed private causal beliefs and ACM.
3. **Cold / reflection:** optional, bounded processing of episodic summaries and strategic plans; optional LLM as advisory module, not source of objective truth.

This is a proposed orchestration policy, not currently a validated human cognitive model.

## Complexity and exactness contract

For `N` agents, `D` registered signal dimensions and `T` ticks:

- Tick scanning: `O(N*T*D)` state checks even when nothing needs attention.
- Sparse wake model: `O((N + A + E)*D + (A+E)*log N)` approximately, where `A` is action-triggering wakeups and `E` external observations. This is a cost model for the simple threshold implementation; not a universal upper bound on future belief graphs.

**Exactness only under declared assumptions.** The experimental prototype has deterministic integer-valued, linearly drifting signals, finite thresholds, one regulation action per agent per tick, bounded cross-need coupling, and pre-sorted external stimulus events. Its priority-queue scheduler computes the same tick at which a trigger becomes eligible. Multi-source nonlinear propagation, uncertain observations, external physics and hidden action opportunities cannot be silently skipped. For unknown dynamics, schedule an upper bound / periodic fallback.

In the experiment, an actor's urgency signal advances lazily from its previous materialized tick; intermediate per-tick calculations are unnecessary *because* the rates are constant and no events occur during the interval. Actions that change other needs must recompute the next deadline.

## Module and security requirements

- Core does **not** hardcode Hunger/Thirst/Anger. `data-packs/pulse-needs.json` defines nine sample signals; `data-packs/pulse-custom.json` extends them with external radiation/curiosity signals. Definitions are namespaced and validated.
- Public API for production should expose `ISignalProvider`, `IDependencyDeclaration`, `INextDeadline`, and `IInvalidationSink`; only trusted modules may supply executable code. Keep a stable C# abstraction and compiled compact signal IDs after registration.
- No plugin may mutate World Authority directly or read hidden truth. A private graph cannot subscribe to events it was not entitled to observe.
- Upon mutation, update all dependent graph nodes in a deterministic stable order. Cap recursion, reject dependency cycles or require an explicit iteration/fixed-point policy.
- Tick ties are ordered by stable agent ID and event phase. Exogenous observations are processed before agent decisions for a given tick.
- Version both module/data pack and actor-specific scheduled events; abandoned queue entries must be invalidated without inventing extra actions.
- Dynamic plugins must be able to declare urgency thresholds and invalidation dependencies without modifying Core.
- Full provenance: `cause-root -> belief revision -> affect appraisal -> goal delta -> decision -> world outcome`.

## Implementation experiment — limited scope

`experiments/Enorys.PulseGraph` is a **standalone, testable numerical signal-scheduling prototype**, not a replacement for the current `Enorys.Core.Runtime.Engine`.

- It compares `FullTickScan` against `SparsePulseScheduler` under **identical integer-valued needs and deterministic stimuli**.
- Registered signals can be extended by an external JSON pack with **no engine code changes**.
- It compares every action count, ordered event hash, and first 12 action summaries; 54 cross-seed test configurations passed in CI.
- Pure reactive regulation is modeled. No evidence graph, complex social behavior, emotions, full planner, multi-zone World Authority, Godot physics or game FPS is benchmarked here.
- Do not interpret a signal-scheduling speedup as the measured overall gain of ENORYS v0.6.

## Upgrade gates before touching default runtime

1. Implement public `ISignalProvider` + a typed subscription registry in `Enorys.Abstractions`, not a hard-coded need array.
2. Replace the hybrid turn-skipping mechanism incrementally; keep a full-tick reference path for replay-parity tests.
3. Attach survival needs with an exact `NextDeadline` implementation. Add observation wakeups for world messages.
4. Attach belief and ACM changes as **private per-agent dirty nodes** with causal/source-root anti-echo.
5. If a future physical/AI effect lacks a safe deadline estimator, give it a bounded fallback cadence; never assume it cannot change an action.
6. Test 50/500/5000 NPC in the **actual** `SurvivalWorld` on identical seeds; verify world ledger, actor isolation, full action trace or explicitly document intentional behavior divergence.
7. At least three workload families: sparse, bursty group communication, and dense combat with all NPCs active. Include the overhead of scheduling, modules, save state and engine adapter.
8. If sparse graph wins, replace heap with a **timing wheel / calendar queue** for many near-term events, benchmark memory and cancellations; **do not** pre-optimise without profiling.
9. Preserve a plug-and-play *tick fallback* for extensions that don't provide safe causal deadlines.
10. Keep batch native C/Zig acceleration optional for dense math, not event orchestration or plugin state.

## Competing approaches and when to use them

- Dense ECS/SoA update loop: potentially superior when essentially every agent must update every tick (combat, close physical sensing). Complementary, not mutually exclusive.
- Incremental dependency graph + deadlines: superior candidate for sparse needs, asynchronous social signals and long-running background NPCs.
- Active-inference/uncertainty-directed deliberation: a **policy model** for deciding what to investigate/learn, not necessarily a low-level scheduler. Requires separate falsifiable evidence-inference experiments.
- LLM-reflection: optional off-critical-path module, not a substitute for a verifiable state machine.

The practical recommendation is **hybrid**: physics runs with the game engine's fixed timestep; agent cognition uses sparse event wakeups; a small set of active NPCs may use high-frequency ECS updates.

Research context: event-based versus time-based simulation, including priority queue tradeoffs; scalable active inference planning is a separate research line.

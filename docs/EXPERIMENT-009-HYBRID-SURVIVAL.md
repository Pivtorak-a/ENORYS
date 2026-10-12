# Experiment 009 — Hybrid PulseGraph in the real ENORYS SurvivalWorld

**Date:** October 12, 2026  
**Status:** implemented, compiled and tested as **opt-in experimental C# engine feature**; **NOT** promoted to the default policy.  
**Measured workflow:** [Hybrid SurvivalWorld Pulse Integration](https://github.com/Pivtorak-a/ENORYS/actions/runs/38211819283).  
**Branch / source:** `src/Enorys.Core/Runtime/Engine.cs`, `PulseCalendar.cs`, `src/Enorys.Domains.Survival/SurvivalWorld.cs`, `SurvivalBackgroundDeadline.cs`, `src/Enorys.Affect.ACM/AffectiveMap.cs`.  
**Reproduce:** `dotnet run --project benchmarks/Enorys.HybridSurvival -c Release -- test` and `-- bench`.

## Why this test matters

Experiment 008 showed a **6.57× win** for a standalone integer need-threshold scheduler with 5,000 NPC and no actual memories, planning, emotional action weighting, inventory or social networks. This experiment answers the harder, more useful question: what happens when PulseGraph is attached **to the actual ENORYS v0.6 SurvivalWorld**?

The result is **mixed and partly negative**. The event calendar eliminates many costly deliberations and substantially reduces short-lived allocation, but at a quality cost: aggressive skipping causes delayed food/water decisions and lower survival health. Under busy social conditions, *total world runtime can become slower*, even with fewer deliberations. A conservative option preserves health much better but may be slower than full deliberation.

## Architecture integrated

```text
                           World Authority
            5,500 actual Survivor instances / objective state
                      |                  |
   500 active, each slot                  5,000 background, deadline driven
                      |                  |
                      +------ Engine ----+
                           |        ^
           ┌───────────────┴─┐      | private notification / deadline
        AgentMind + Plans   ACM      |
           root-based evidence      IPrivateWakeFeed
           private observations <--- World.Send / scenario events
                      |
                 ActionProposal
                      |
           SurvivalWorld.Execute validates
                      |
            daily needs, shared resources
```

- Background actors retain **the same live `SurvivalWorld`**, supplies, inventory, needs, private `AgentMind`, plans and social events as active actors.
- The active 500 agents deliberate each of their own turns; up to 5,000 distant NPCs can skip cognitive *evaluations* but their **world time and physiological change never pause**.
- `IWorldFastForward.AdvanceSlots()` jumps across inactive background actor slots while preserving each day boundary and scheduled exogenous stimuli.
- `IPrivateWakeFeed` supplies *non-consuming* messages; a sleeping NPC wakes when it receives evidence, help requests or gifts.
- `ICognitiveWakeSignal` lets any registered cognitive module veto an overly long sleep. The optional `AffectiveMap` implements this; private emotional episodes have their own lazy decay. Actor-local epistemic records, unfinished plans, help requests and unsettled debt conservatively keep their owner awake.
- A registered `IBackgroundDeadlinePolicy` selects sleep rounds using namespaced, validated user-defined thresholds. It can be replaced by an independent plugin without rebuilding the Core.
- Two implementations are tested: `PulseCalendar` (min-heap) and `PulseWheelCalendar` (actor-slot-aligned deadline wheel). **They produce identical action traces** for matching seeds and wake policies.
- Active/background agents are interleaved among roughly 32-person zones. Previously, building neighbor lists over 5,500 actors would incur quadratic work; the adapter now uses indexed zone membership and indexed incident lookup.
- `NormalizeCognitiveClock=true` makes memories' evidence-decay horizon and affective exponential decay expressible in **actor rounds**, not raw total NPC steps. Without this correction, increasing population from 24 to 5,500 NPC would spuriously accelerate forgetting.

### Accuracy boundary: approximation, not exact equivalence

With **maximum sleep rounds = 1**, the hybrid calendar and the full C# engine yielded **exactly identical ordered action trace hashes** and authoritative material/health states on three matched smaller-world seeds (17, 42, 81). This verifies the integration and prevents hidden extra actions.

When background actors sleep for multiple rounds, **behavior is intentionally different**. The shared objective world and all data laws remain the same, but an NPC will miss intermediate *opportunities* to get food, drink or socialize. Do not equate fewer computed decisions with a free speedup of identical cognition.

The simulation uses the same deterministic seed and same timed externally authored message stream for baseline and hybrid modes, but social actions and resources diverge **endogenously** after skipped deliberations. The metric "time per identical decision" is therefore not applicable.

## Benchmark design

- Hardware: GitHub-hosted Ubuntu 24.04 x64; .NET 10 Release; shared CI machine (noisy).
- 500 active + 5,000 background NPC, *all real C# `Survivor` objects*.
- Seven game days × three agent turns/day → **115,500 nominal actor slots** per run.
- The full engine deliberates all slots; hybrid is opt-in.
- Optional ACM **enabled in every comparison**, and social behavior/planning both active.
- Three seeds (100, 101, 102); execution ordering rotated so full engine is not always first. Short and large warmups performed.
- Two social workloads: low witness visibility (0.025) and higher witness visibility (0.18). Each includes timed private eyewitness report, gift, and retraction.
- Media below are **medians of three distinct seeded world runs**, not a statistically precise runtime distribution or controlled local-machine benchmark.
- Temporary allocations measured with `GC.GetTotalAllocatedBytes()`, excluding world construction and before-run resource setup. Peak/working-set memory may differ and is not the same as allocated bytes.

## Final measurements: quality and performance together

### Scenario A — low-visibility / lower social activity

| Mode | Median total time | Mean temporary allocation | Avg skipped cognitive slots | Median NPC health (0–100) |
| --- | ---: | ---: | ---: | ---: |
| Full C# cognition | **738.27 ms** | 812.70 MiB | 0 | **91.48** |
| Hybrid heap, aggressive wake profile | 774.86 ms | 587.92 MiB | 42,230 (36.6%) | 83.72 |
| Hybrid timing wheel, aggressive | 738.45 ms | 587.72 MiB | 42,230 (36.6%) | 83.72 |
| Hybrid timing wheel, **conservative** | 877.26 ms | 741.09 MiB | 19,330 (16.7%) | **91.07** |

Result: the wheel effectively **matches**, but does not significantly beat, full Cognition time. The aggressive wake policy creates ~27.7% fewer temporary managed allocations, but loses ~7.8 mean health points — an unacceptable trade without explicit game-design choice. Conservative policy brings the health gap down to ~0.4 points, but runs ~18.8% slower.

### Scenario B — denser social observation

| Mode | Median total time | Mean temporary allocation | Avg skipped cognitive slots | Median NPC health (0–100) |
| --- | ---: | ---: | ---: | ---: |
| Full C# cognition | **1,088.05 ms** | 914.01 MiB | 0 | 88.89 |
| Hybrid heap, aggressive | 1,195.94 ms | 786.94 MiB | 30,103 (26.1%) | 85.69 |
| Hybrid timing wheel, aggressive | 1,159.65 ms | 786.67 MiB | 30,103 (26.1%) | 85.69 |
| Hybrid timing wheel, **conservative** | 1,227.44 ms | 836.16 MiB | 18,665 (16.2%) | **89.02** |

Result: under social activity all hybrid options are **slower**, even though they reduce allocated bytes. Conservatively scheduled NPCs have similar (in this sample slightly better) health than baseline, but incur a time penalty.

Every seven-day run retained 5,500 living agents and passed exact **inventory/resource-ledger conservation**. Full vs hybrid state equality is **not claimed**, nor would it be expected when sleeping changes agent actions.

### Private cognition does work

On one seven-day low-visibility seed with aggressive wakeups:
- 500 active NPC deliberate at all **10,500** of their scheduled slots.
- Background NPC deliberate ~62,860 times instead of 105,000.
- **42,140** slots are skipped with authoritative time fast-forwarded in **10,073** batches.
- **510** explicit early wakes result from observed private messages.
- **519** candidate sleeps are shortened by registered cognitive state wake vetoes, including the ACM.
- ~10,935 message visits, ~3,399 private evidence/retraction updates, and corresponding ACM message appraisals are measured. This is real private state within the existing ENORYS memory and emotion modules, not a hand-coded numerical stand-in.
- Background actors receive scripted and spontaneous messages; no global truth is copied into their private view.

These counters describe *processing occurrences*, not 10,935 unique independent witnesses or psychologically validated knowledge.

## Interpretation and decision

1. **Keep default ENORYS cognition on the existing deterministic full/legacy mode.** Do not claim 6.57× improvement from Experiment 008 for a complete real NPC engine.
2. **Retain PulseGraph as an opt-in integrated LOD experiment**, because it demonstrably cuts temporary allocation (~9–28%, depending on profile) and can reduce cognitive activations while preserving world invariants and subjective messages.
3. **Conservative profile is the only acceptable starting default for a quality-sensitive LOD experiment**: measured mean health stays within about 0.5 points of full cognition, but not faster today.
4. **Timing wheel is not proven consistently faster than heap**; both are retained as alternatives for controlled measurement. This is an orchestration/data-layout question, not a justification for rewriting to C or Zig.
5. **Do not optimize only the queue.** Most real costs are candidate generation, shared world observation, social updates, allocation in memory/ACM and the different endogenous number of events. In socially dense scenarios, extra rumor propagation can offset savings.
6. **Next real breakthrough candidate:** a *provably safe* `IActionInactivityCertificate` or "doze/macro-action" contract, where an actor may skip only if the decision module provides an earliest possible change bound on the **winning action and its required side effects**. Alternatively, execute cheap automatic need maintenance as an explicit action while deep cognition sleeps. Test against identical action-quality, health and resource constraints.
7. **Adaptive hybrid mode:** run sparse scheduling for isolated regions and dense ECS for high-contact zones; use observed activation density with hysteresis. Test at least combat-like bursts before declaring any speed advantage.

### Limitations

- No actual Godot frame rendering/physics or thousand-person collision/pathfinding; all figures are headless.
- No scientifically calibrated emotional behavior or long-lived persisted snapshot across runs; observed health is only one coarse behavior-quality indicator.
- Test world zone layout for 5,500 NPC is optimized and differs from old small-world four-zone placement. Comparisons within this test use **the same** layout and initial conditions.
- After sleep >1 turn, different actors may gain/lose access to shared resources and rumors. A score-only comparison does not prove equivalent narrative outcomes.
- Workload and thread/tiered JIT noise matters; results should be repeated on dedicated hardware before deployment.
- Trusted in-process C# plugins are not a sandbox for untrusted code.

## Commands

```bash
dotnet run --project benchmarks/Enorys.HybridSurvival -c Release -- test
dotnet run --project benchmarks/Enorys.HybridSurvival -c Release -- bench
dotnet run --project tests/Enorys.Integration -c Release -- test
```

The additional acceptance gate requires the conservative profile's seven-day mean health to remain within **1.5 points** of full cognition for each of the specified seed/scenario pairs, with equal living-agent counts and balanced materials. It is a **scenario-specific gate**, not a universal proof of cognitive fidelity.

# ADR-0005 — Opt-in Hybrid SurvivalWorld Cognitive Scheduling

**Decision date:** 2026-10-12  
**Status:** Accepted as a **research feature, disabled by default**. This does not supersede the current headless default runtime or claim performance superiority.  
**Experimental report:** [Experiment 009 — 5,500 real NPCs](EXPERIMENT-009-HYBRID-SURVIVAL.md).  
**Related:** [ADR-0003 — externally extensible modules](ADR-0003-EXTENSIBLE-MODULES.md), [ADR-0004 — PulseGraph](ADR-0004-PULSEGRAPH.md).

## Decision

Retain the C#/.NET 10 authoritative world and cognitive-module boundary. Add **optional** hybrid cognition with a *static 500-active/5,000-background split for the reference workload*. Background-agent activation is controlled by a validated external data pack and one registered `IBackgroundDeadlinePolicy`. The core does not embed a fixed set of needs, items, emotion names, survival thresholds or real-world truth.

**No replacement of the default scheduler** until action-quality-sensitive benchmarks prove a clear benefit and psychological/world invariants remain acceptable.

### Authority and isolation

- `IWorldAdapter.Execute` alone changes objective resources. Actor modules only propose actions using filtered `ObservationPacket` and private `AgentView`.
- `SurvivalWorld` owns physical time and emits non-consuming wake signals to `IPrivateWakeFeed`; `Engine` decides which agent needs its cognition updated.
- `IWorldFastForward.AdvanceSlots` preserves day boundaries, survival deterioration and scheduled trusted world stimuli, even when no NPC is thinking.
- The world can declare `IWorldScheduledStimuli.NextExternalStimulusTick`; the scheduler **must not jump past** such an event.
- `ICognitiveWakeSignal` is optional, but if installed its earliest-change bound can only shorten sleeping. Our optional ACM registers one to prevent its significant emotional episodes from being ignored.
- Private evidence and social commitments can also force short deadlines; this maintains provenance privacy and does not authorize omniscient NPC knowledge.

### Calendar alternatives

- `PulseCalendar` uses a priority queue, versioned entries and deterministic index-ordered due ticks.
- `PulseWheelCalendar` uses actor-slot alignment and direct deadline arrays. It avoids heap operations but scans possible background slots. Tested for exact action replay against heap on same seed.
- The per-agent deadline strategy is selected by an external policy; both calendars accept the same policy.

### Reproducibility and truthfulness

- One cognitive round corresponds to a **full world actor round** rather than one global actor slot when `NormalizeCognitiveClock=true`. Actor-level evidence and ACM decay use this consistent time unit.
- At `BackgroundMaximumRounds=1`, active/paused hybrid and dense mode produce the **same ordered action history** and resource/health results for the tested 24-NPC cases.
- At larger sleep intervals, **deliberations and actual actions can be omitted**. This alters survival dynamics, influences social propagation and is *not* an exact program transformation.
- Quality indicators include health, deaths, balance of physical inventory, number of messages/evidence updates, and deterministic repeatability. No benchmark of a mode with fewer actions shall be described as a speedup of the exact same cognitive trajectory.

### Data-driven tuning

- `data-packs/survival-pulse.json`: aggressive background thresholds; largest reduction of decisions, lower health in seven-day tests.
- `data-packs/survival-pulse-conservative.json`: earlier wakeups, maximum 3 rounds between decisions; seven-day health within 1.5 points of full reference in all tested scenarios. However, **it currently runs more slowly**.
- Both packs can be edited/extended without changing the engine's C# core; future domain plugins may replace their `IBackgroundDeadlinePolicy` entirely.

### Release gates for default promotion

1. Add a formally defined `IActionInactivityCertificate` to signal a **provable earliest change in the optimal action**, rather than heuristics on isolated needs. A plugin lacking such a certificate falls back to dense evaluation.
2. Verify missed-actions and action consequences explicitly. If using macro-actions, execute those actions via World Authority and record them with clear provenance.
3. Benchmark active 500 + background 5,000 on fixed hardware across sparse and combat-density event workloads; include independent physical sensing, Godot frame timings, memory, GC pauses and actor starvation.
4. Keep separate *exact replay mode* and *approximate simulation LOD mode*. Test actor-specific information boundary before and after every world action.
5. Automatically switch busy areas into dense mode using observable event density and hysteresis; avoid any unsound prediction that hidden world changes cannot matter.
6. Achieve a meaningful whole-world runtime improvement at a predeclared bound on survival/gameplay quality. Current results **do not** pass this gate.
7. Add cancellation/backpressure caps for incoming private message queues and emotional episode retention before untrusted external modules are allowed.
8. Establish snapshot format and migration path including registered signal/data pack versions and pending wake calendar entries.

## Out of scope

No rewrite to C, Zig, Go, GPU or custom physics engine. This experiment tests scheduling/causal invalidation, not a new numerical algorithm or a general human cognitive psychology theory.

**Final architectural conclusion:** keep modular, world-authoritative C#, support both sparse event-driven and dense cognition, but do **not** advertise the current heuristic skip strategy as faster or behavior-preserving. Improve action-validity certificates, cache locality and cognitive state indexing next.

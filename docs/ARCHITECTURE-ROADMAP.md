# ENORYS — architecture roadmap after Experiment 003

**Decision (2026-10-11):** Continue with a standalone headless **C#/.NET 10** cognitive kernel. Use the browser's `experiment-001/002/003` scripts solely as falsifiable exploratory sandboxes. Do **not** treat JavaScript mechanics or parameter values as a stable SDK API, and do not duplicate the final simulation domain inside the cognitive core.

## Reference architecture

```text
Simulation host (SimWorld / CLI / Godot / third-party)
  ├─ World Authority: locations, items, owners, physics, weather, events
  │  └─ Visibility & SensoryAdapter: observer-specific, possibly wrong messages
  ├─ ENORYS Runtime: tick queue, stable IDs, budgets, keyed RNG, event replay
  │   └─ AgentState per NPC (own data; never a World reference)
  │       ├─ Homeostasis / NeedState: deficits, health/injury/illness exposure
  │       ├─ Memory / EvidenceGraph: rooted observations, messages, recall
  │       ├─ Semantics / SocialState: property, consent, debts, trust, norms
  │       ├─ GoalManager / Planner: plan prerequisites & multi-step intent
  │       ├─ Decision: habits → priority gate → bounded deliberate utility
  │       └─ ActionProposal + DecisionTrace
  ├─ World Validator: atomic command commit → objective event / rejection
  └─ Debugger & UI: omniscient world trace + separate subjective agent inspector
```

The **need dynamics / actions / item catalog belong to the game-domain package** (e.g., `Enorys.Domains.Survival`) and are passed to the general cognitive kernel via typed observations/goals/effects. This prevents the reusable ENORYS brain from depending on the existence of food, water, zombies, Godot, or the fixed 14-item list.

## Data contracts (planned; no false claim of implementation)

- `AgentId`, `ItemTypeId`, `EntityId`, `StableEventId`, `Tick` have canonical serialization and no mutable global lookup shortcuts.
- `NeedDefinition`: value range, rate rule, critical thresholds, dependent effects, observable signal, measurement unit, validation constraints. Domain-defined; not one hard-coded 100-point formula for all possible needs.
- `ItemDefinition`: stackability, mass/volume, consumable or durable, quality, contamination, temperature, tool capabilities and crafting recipes. Item **instances** retain owner, location, durability, reservations and provenance.
- `ActionDefinition`: preconditions, observed requirements, actor cost, world effect intent, rollback policy, legal/social norms, predicted need relief; domain-registered.
- `Observation`: private claims and source reliability, not world facts; event-to-source mapping diagnostic only.
- `ActionProposal`: target, expected result, prerequisites and plan parent; accepted/rejected only by World Authority.
- `DecisionTrace`: mode, considered/blocked actions, estimated utility and evidence root IDs; replayable.
- `EffectLedger`: complete inventory deltas, conversions, wear and spoilage; atomic, numeric invariants.

**Security/causality boundary:** a NPC cannot read the authoritative incident actor, another agent's private inventory, or unobserved locations. Reading another's public request is valid only after delivery through a communication event. Persist world truth separately from agent snapshots.

## Near-term plan with acceptance gates

### v0.4 — Typed boundaries + parity test

1. In .NET, implement `IWorldAdapter`, `ObservationPacket`, `AgentSnapshot`, `ActionProposal`, authoritative atomic executor and canonical trace. No game-engine dependency.
2. Port the 9 need definitions and 14 item definitions to **data in a survival domain**. Include world-owned structure objects and item location/ownership; distinguish consumable vs durable objects.
3. Property tests for no unseen facts, zero negative inventories, atomic rollback, durability wear, conversions, one-source rumor deduplication and event replay. Compare the same small fixed scenario against the JS reference trace by semantic expectations, *not* by matching RNG implementations accidentally.
4. Keep a small CLI runner for public reproducibility. Release gate: repeated identical input traces + finite values + all conservation checks across 100+ fuzzed runs.

### v0.5 — Multi-step goals and belief revision

1. Add planning states: `Want clean water` → `obtain dirty water` → `acquire wood` → `boil` → `drink`. Track reservations and interrupt priorities (injury, threat), allow replan after world rejects action.
2. Add memory retrieval, aging evidence, speaker credibility and explicit retraction; prevent looped rumor influence.
3. Add social contracts, request/accept/refuse, debt, gifts, exchange, role obligations, group membership and consent; split `Owns`, `Holds` and `Access`.
4. Compare reactive v0.3 with planner-enabled version on **held-out** seeds by survival, unused resources, failed action rate and explainability. Do not tune directly on test seeds.

### v0.6 — Cognitive LOD and performance

1. Automatic fast path for stable low-salience tasks; deliberative path only for novel high-cost decisions; per-agent CPU budget and event wakeups.
2. Parallelize only pure read-only inference after deterministic ordering and replay are demonstrated.
3. Benchmark 50/100/500 cognitive agents; report CPU/allocations/tick, trace sizes, cold/warm cache and GC pause distributions on documented hardware. Avoid claiming a target FPS without a measurement.
4. Implement an opt-in Godot adapter for world events/observations/actions; render only needed NPCs while offscreen simulations continue.

### v0.7 — Longer stories and community tooling

1. Persistent memories and agent snapshots with versioned save/migrations, per-NPC story playback and user-selectable viewpoint (partial vs omniscient).
2. Reproducible scenario packs, intervention comparisons, machine-readable logs and tests contributed by other developers.
3. Optional language-model advisor for long-horizon abstract plans and dialogue **outside** time-critical decision loops. Require attribution of suggestions, strict action validation and budget controls.

## Decisions to postpone

- Full ECS/SoA memory layout everywhere, Rust/C++ rewrite, one LLM per NPC, arbitrary JSON rules, cross-machine bitwise deterministic floats, personality-evolution formulas or simulated consciousness claims. Only introduce these after proving they improve specified metrics.
- Numeric thresholds and trait distributions are **configuration under test**, never unquestionable constants or a statement about humans.

## What to measure before claiming success

**Functionality:** percentage of world actions with a grounded private evidence chain; number of impossible/hidden-world-dependent choices (goal 0); resource ledger discrepancy (goal 0); fraction of replay mismatches (goal 0 on the same pinned build/platform); blocked actions and reason codes.

**Behavior:** survival and unmet needs in held-out scenarios; diversity of actions; fraction of redundant gathering; number of multi-step plan completions; false accusations per actual incident; percent of changes explained by a recorded observation/intervention.

**Computational:** total milliseconds per 1,000 candidate evaluations, p95 tick latency, allocations per NPC step, bytes per memory event, 50–500 NPC scalability. Collect from a real headless C# implementation only.

## Main risk exposed by Experiment 003

Reactive utility often optimizes the next action but not the **prerequisite chain**. Under scarcity, NPCs can repeatedly scavenge and neglect longer plans (making water safe, building effective shelter). The next core subsystem should therefore be **GoalManager + bounded planner with resource reservations**, not more emotions or a large LLM. Preserve epistemic isolation and authoritative world execution as non-negotiable constraints.

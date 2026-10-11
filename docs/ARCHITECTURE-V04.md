# ENORYS v0.4 architecture, four sequential gates

**Scope:** executable JS *reference architecture* for the future standalone C# kernel.

```text
Simulation host / web/CLI
  -> World Authority (src/v04/world.mjs)
       -> private observation packets and authenticated message delivery
  -> Memory & Social Epistemics (src/v04/memory.mjs)
       -> actor-owned evidence, belief provenance, trust, debt, correction
  -> Goal Manager (domain goal registry in src/v04/planner.mjs)
       -> bounded prerequisite expansion over domain recipe graph
  -> Cognitive LOD (src/v04/lod.mjs)
       -> guarded automatic action OR full deliberate scoring
  -> ActionProposal --> World Authority atomic validation --> Event
```

### Core invariants

1. The actor's decision signature is `(agentPrivateState, filteredWorldPacket, options)`; it never receives the authoritative `world` argument.
2. Domain definitions (need names, item categories and recipes) are separate from world mutation and actor deliberation.
3. `execute` checks complete action legality **before** mutating inventories; every authorized conversion updates an auditable resource ledger.
4. `makePlan` never reads private inventories of others or world history; it expands public source affordances and local inventory, not guaranteed knowledge of distant material availability.
5. Plan reservations are **dynamic**, protecting newly acquired components for future recipes.
6. Original eyewitness evidence roots survive forwarding. Multiple messages with the same root do not count as independent observations. Authorized corrections deactivate prior received claims.
7. Cognitive LOD bypasses full deliberation **only** when scalar guards exclude alternative winning actions. Incoming packets, conflicts, infeasible plans and urgent conditions force full evaluation.
8. Canonical outputs for same seed/settings/build are reproducible. Binary cross-platform determinism is **not yet guaranteed**.

### Modules and compatibility

- `src/v04/domain.mjs` — survival domain catalog, recipes, need rates, typed delta helpers (stage 1).
- `src/v04/world.mjs` — deterministic authoritative world, filtered perceptions and validated mutations (stage 1).
- `src/v04/planner.mjs` — bounded domain-neutral recipe planner, local item reservations (stage 2).
- `src/v04/memory.mjs` — private evidence, retract, trust, requests, debts, provenance (stage 3).
- `src/v04/cognition.mjs` — local scoring, socially aware actions and goal integration (stages 2–3).
- `src/v04/lod.mjs` — conservative automatic-vs-deliberative gate (stage 4).
- `src/v04/runtime.mjs` — headless orchestration, instrumentation, reproducible world summaries.
- `scripts/experiment-004.mjs` — held-out trials and microbenchmarks.
- `web/evolution.js` + `evolution.html` — interactive reader with counterfactual planning baseline.

**Project state:** the production C# `src/Enorys.Core` contains separate mathematical primitives, not this JS runtime. Do not claim they are unified or tested as a full C# system.

### Longer-term production architecture

Adopt an external survival domain package `Enorys.Domains.Survival` with item *instances* and transactional effects; stable semantic contracts; event-sourced world and agent-local journals; incrementally recomputed beliefs; a bounded goal-stack planner; a deterministic per-agent wake scheduler, event-triggered attention and cognitive LOD budgets; dedicated deterministic test runner and Godot world adapter. Preserve `World Authority` as the **only** place allowed to mutate objective facts.

See `EXPERIMENT-004.md` for verified numbers and performance limitations. Do not extrapolate current JS microbenchmarks into Godot FPS.

# ENORYS Experiment 004: Four sequential architecture gates

**Date:** 2026-10-12 · **Status:** verified **JavaScript headless research sandbox**, NOT an integrated C#/.NET runtime.

## Executive result

Four sequential prototypes have been implemented, corrected where tests exposed problems, and validated on seeded multi-agent simulations:

1. **G1 — Domain boundaries:** `src/v04/domain.mjs` defines nine needs, 14 types of goods, recipe constraints and numeric rules separately from cognition. `world.mjs` owns inventory, sources, weather and atomic effects; cognition receives only agent-owned state plus a filtered packet. Unsupported action proposals are rejected before mutations. This is a JavaScript architecture demonstration; the previously created C# primitives remain a *separate* experimental core.
2. **G2 — Multi-step planning:** a bounded recursive prerequisite planner (`planner.mjs`) finds chains such as *collect dirty water → gather wood → boil → drink*. Plans persist, can be invalidated by depleted sources and have dynamic ingredient reservations to protect future crafting. Rejection clears plans for reconsideration. Planning-enabled vs reactive runs are paired using identical seeds.
3. **G3 — Episodic, social evidence:** rooted claims, one-source rumor deduplication, subjective confidence, retractable testimony, trust changes, aid requests, actual gift deliveries, recipient-recorded obligations, repayments, accusations and investigations. No NPC is allowed to read hidden World Authority history. Full group politics, robust source correlation and long-term autonomous personalities are NOT implemented.
4. **G4 — Cognitive LOD:** the automatic mode can continue an existing plan only when a conservative utility-ranking guard rules out a competing action. Otherwise it deliberates. New observations, unresolved social claims and critical risks trigger reconsideration. Correctness against full-policy shadow decisions was explicitly tested; speedup is **not** guaranteed for large social groups. This is not yet a fixed CPU-budget scheduler or truly event-driven runtime.

## Mathematical and held-out protocol

`node scripts/experiment-004.mjs docs/experiment-004-results.json` runs 40 paired **held-out seeds 1000–1039**, 24 agents per run, 14 days, three turns per day, 55% resource supply. Four variants share the same seeded initial conditions. This produces **160 full simulation runs**. In the module tests, another 100 seeds (101–200) stress varying resource supply, visibility and cold; 100 other seeds (1000–1099) were independently audited against full deliberation.

**Outcome averages, 40 runs per row** (rounding to three decimals):

| Variant | Mean health /100 | Mean thirst /100 | Completed plans | Candidate checks | Auto decisions | Gifts | Repayments |
|---|---:|---:|---:|---:|---:|---:|---:|
| Reactive, no planner | 35.249 | 85.708 | 0 | 7052.600 | 0 | 1.625 | 0.875 |
| Planner, full deliberation | 53.289 | 71.828 | 58.100 | 7449.900 | 0 | 1.225 | 0.950 |
| Planner + LOD + social | 53.289 | 71.828 | 58.100 | 7027.325 | 54.250 | 1.225 | 0.950 |
| Planner + LOD, social disabled | 72.611 | 64.598 | 75.300 | 5812.850 | 159.925 | 0 | 0 |

- **Planned vs reactive:** health improved **18.040** points (the average of the measured paired scenario runs). Mean thirst fell **13.880** points. This reflects the selected gameplay heuristics, not scientific human outcomes.
- **LOD vs full deliberation:** same aggregate outcomes in the tested sample; **422.575** fewer candidate evaluations on average (**5.67%**). Automatic planning steps averaged 54.250 out of ~1008 turns; **not** all decisions qualify as automatic.
- **No social behavior vs social behavior:** health +19.322 points without social decisions; the toy agents spend more time obtaining food and water rather than asking, repaying or accusing. This is a *tradeoff in the utility design*, not evidence that social behavior is bad in real communities.
- **Causality and replay:** the audit checked **5,294 automatic selections** over 100 separately seeded runs and found **zero** mismatches against shadow full-deliberation choices. This is an empirical finite test, not a formal proof for all possible input states.

**Important distinction:** If group visibility, starting resources, social credibility, or utility weights change, these effects may change or reverse. No population-level behavioural validation has been performed.

## Longer social chain (seed=42)

A genuine run with 24 agents, 18 days, 50% supplies, 55% witness probability and 18% witness misidentification generated resource-taking incidents, transmitted evidence, investigations and accusations, along with material request/gift/repayment cycles. One character's message can affect another's later choice because claims enter private memory. Actions are selected by utility and plan feasibility; there is **no encoded chapter list or forced narrative ending**.

For exact events and the full witness/trust/plan traces, run `node scripts/experiment-004.mjs`, open `evolution.html` from a local HTTP server, or inspect `docs/experiment-004-results.json`.

## Verification gates (all functional gates passed)

- 28 newly added Node automated tests for G1–G4, 15 original Experiment-003 tests also passed locally (the entire repository CI suite must additionally remain green).
- 100 varied seed simulations: **zero negative inventories, zero resource ledger discrepancies, finite bounded needs, completed event counts**.
- Unauthorized mutation checks: world rejects fabricated rumors, invalid recipe execution, unsupported accusations, and remote gifts/requests.
- Received testimony can be retracted only by the authenticated sender; claims with identical original roots are deduplicated.
- Dead agents do not issue further actions: found and fixed during multi-seed testing.
- Dynamic resource reservations: found and fixed a bug where water-plan ingredients acquired *after* a plan was created could still be given away.
- LOD: early candidate bypass artificially changed behaviours. Changed to conservative action-ranking guard, then shadow-verified 5,294 automatic selections with zero decision mismatches.

## Performance: deliberately qualified

A Node.js **v22.16.0**, x64 Linux shared-sandbox microbenchmark, eight repeats per row, 14 simulated days and three turns per day found indicative mean elapsed times:

| Agents | Full deliberation | Cognitive LOD | Average automatic steps |
|---|---:|---:|---:|
| 50 | 25.385 ms | 15.511 ms | 67.875 |
| 100 | 33.398 ms | 30.879 ms | 33.000 |
| 500 | 390.466 ms | 362.443 ms | 0.375 |

**Do not treat these as statistically robust performance claims.** The process runs on shared hardware, different runs experience warmup/GC variability, and at 500 agents automatic selection is virtually absent. The 500-agent difference is most likely environmental noise, not LOD scaling. Smaller 5-day runs even showed a slowdown with LOD. As the social graph becomes dense, enforcing correctness frequently forces full deliberation. **The large-scale speedup gate remains open.** See `docs/experiment-004-benchmark-14d.json` for per-run methodology and timings.

## Known limitations and next architectural decisions

- **Not yet C# integrated:** No .NET world-adapter and no package that SimWorld/Godot can import as a full cognitive engine. JS is an executable reference sandbox only.
- **Social costs and unmet needs:** hygiene, safety and morale can saturate; the toy decision scoring does not balance all needs. Requests/repayments can crowd out survival tasks; needs require calibration and long-term cost models.
- **No item instances:** categories are stack counts, not unique objects with durability/ownership/location/contamination. The true production domain must support unique objects with typed properties.
- **Finite evidence handling:** dedup by exact root prevents direct rumor echo but distinct correlated roots may still cause confidence inflation. Source-independent inference remains unverified.
- **No bounded CPU quota or event-wakeup queue yet:** automatic mode is only a guarded fast path, not a global scheduler. 500-agent scale must be benchmarked with actual .NET architecture and indexed spatial queries.
- **No persistence or live Godot adapter:** snapshots are currently only in memory, and browser simulation is independent from the C# kernel.
- **No learning of preferences:** agent trait weights are seeded and static; no ML or LLM autonomy is claimed.

## Recommended next release

1. Port the **stable boundary contracts** from the tested JS experiment to .NET 10: `ObservationPacket`, `ActionProposal`, `WorldResult`, typed `NeedId/ItemId`, `ActionDefinition`, and an explicit `IWorldAdapter` interface.
2. Build a headless .NET scenario integration test with a property-based harness and an isolated *real game domain*; compare **semantic invariants**, not accidental JS/.NET RNG equality.
3. Add a persistent evidence/provenance DAG and event-sourced replay. Detect correlated sources, support real corrections, group consensus and contract/ownership rules.
4. Replace the global round-robin scan with spatial indexing and a per-agent wakeup queue, bounded deliberation and budget-driven Cognitive LOD; benchmark 50, 100 and 500 agents in .NET before Godot integration.
5. Only then add Godot adapter, 3D perception, object instances, zombies and in-world behaviour.

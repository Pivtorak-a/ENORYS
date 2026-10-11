# ADR-0001: ENORYS headless runtime architecture

**Status:** Accepted as a provisional architecture for PoC v0.2, 2026-10-11.
**Owner:** Project maintainer.
**Origin:** NOEMYN Cognitive PoC v0.1.1 mathematical audit, 2026-10-09.
**Scope:** Engineering architecture, NOT a claim of psychological realism.

## Decision

Build a **headless modular monolith in C# / .NET 10 LTS** with a strict separation between:

1. **World Authority** (external): truth, physical state, action validation, and authoritative events.
2. **Perception Adapter**: constructs partial, fallible agent-specific observations, never exposes private world truth.
3. **ENORYS Core**: individual subjective belief/memory state, causal inference, semantics, appraisal and decisions.
4. **Deterministic Runtime**: scheduled execution, bounded budgets, keyed RNG, traces and replay.
5. **Presentation Adapters**: Godot, CLI, test sandbox, or optional other engines. No Godot reference inside core.

The existing JavaScript sandbox remains a **separate visual proof of concepts**, not the main cognitive runtime. Implementing and testing mathematical primitives in .NET does not mean the complete architecture is implemented.

### Why this over alternatives?

| Option | Advantage | Cost / rejection |
|---|---|---|
| C#/.NET headless modules **(selected)** | Works with pure CPU tests, mature tooling, Godot C# desktop interoperability, reasonable iteration speed | Runtime allocations and cross-platform float replay need profiling/controls |
| All cognition inside Godot nodes | Faster visual integration | Couples agent reasoning to scene lifecycle and renderer, expensive for offscreen agents |
| Full C++/Rust ECS first | Potentially optimal hot-path control | Higher complexity before psychological/epistemic models are even validated |
| Extend browser JavaScript prototype as the engine | Very accessible demo | Current proof is purposely simplified and shouldn't dictate long-term data contracts |
| Microservices / an LLM per NPC | Separately scalable services | Network overhead, coordination cost, nondeterminism and severe latency per decision |

We will **not** migrate to ECS/SoA everywhere simply because it sounds efficient. First use typed records and arrays, then introduce per-predicate SoA, pooled buffers or blittable structures **only for measured hotspots**.

## Authoritative pipeline

```text
World State + external events
  -> Perception.Filter(observer, visible history)
  -> Observation (agent-local; may be wrong or incomplete)
  -> Attention + episodic trace level
  -> Beliefs / Evidence lineage / causal hypotheses
  -> Social norms + appraisal + goals
  -> Cognitive scheduler (cheap automatic / bounded deliberate)
  -> ActionProposal + DecisionTrace
  -> World.ValidateAndExecute(ActionProposal)
  -> ActionResult + new filtered observations
```

**Invariant A — epistemic isolation:** an agent's decision method accepts **only** its internal state and its own filtered observation/state messages, never the global fact store. Internal diagnostic event identifiers must not be resolvable into ground truth by an agent.

**Invariant B — world authority:** the brain never writes external resources, relationships of other agents, locations, or facts. It only issues a proposal; the adapter either validates and applies it atomically or rejects it.

**Invariant C — evidence ancestry:** every observation/message records an origin that can be inspected. A repeated report with identical root and claim hash does not become a second independent evidence item. Multiple *different* claims sharing one root may be dependent and require explicit correlation handling.

**Invariant D — reproducibility:** given an identical binary, platform, external event sequence, configuration and seed, replay must yield an identical canonical trace. The RNG is keyed by `(seed, entity, tick, channel)`; calls from unrelated agents cannot perturb each other. Cross-runtime/CPU bitwise equality is **not** assumed.

**Invariant E — explainability:** every deliberate choice records evaluated alternatives, utility terms, eliminated actions and selection parameters. Automatic choices have a habit/trigger trace instead of invented utility terms.

**Invariant F — no story scripts:** no conditional on named story, named person, scripted quest or particular episode. Domain action types and norms may be data-driven later.

## Boundaries and types (implementation plan)

- `WorldEvent`: `StableId`, `Tick`, `Actor`, `ActionKind`, list of authoritative fact deltas.
- `Observation`: observer ID, perception quality, private claims, source class, time, epistemic provenance; **not** a masked copy of ground truth.
- `FactKey`: typed predicate and all arguments (up to three initially); distinguish absent fact from `false`.
- `Belief`: posterior/hypothesis, known origin roots and dependencies, confidence vs fidelity separated.
- `ActionProposal`: actor, action kind, target, expected outcomes, decision ID.
- `ActionResult`: success/failure and **agent-visible** outcome; private rejection rationale stays with world diagnostics.
- `DecisionTrace`: automatic/deliberative/no-action mode and the relevant evidence, constraints and scores.

Proposed folders when implementation warrants them:

```text
src/Enorys.Core/
  Runtime/            keyed RNG, scheduler, budgets, traces
  Epistemics/         evidence ancestry, belief updates, causal inference
  Memory/             episodic index and delayed recall
  Semantics/          ownership, consent, norms, relationship models
  Psychology/         motives, appraisal, bounded emotions
  Decision/           automatic habits + bounded deliberation
  Communication/      typed tell/ask/accuse, trust, ToM-1
src/Enorys.Adapters/  [later] world contracts, CLI, Godot binding
tests/Enorys.Smoke/  numerical and contract validation
benchmarks/           [later] isolated performance measurements
web/                  independent browser demonstration
```

Folders not yet implemented are plans, **not empty scaffolds**.

## First mathematical mechanisms

- **Noisy-OR exact enumeration:** maximum eight *independent* binary causes, 2^8=256 configurations, explicit leak. Track `P(no listed causes | effect)` separately from `P(leak succeeds | effect)`; they are not the same event.
- **Evidence provenance:** key same-origin claims by `(rootId, contentHash)` and disallow double-counting exact repeats. This small component does not resolve all correlated-source inference.
- **Stable softmax:** subtract the maximum utility before exponentiation; reject NaN/Infinity; define deterministic tie-break for near-zero temperature.
- **Violation penalty:** severity × believed lack of consent × lack of legitimacy × norm internalization. Do **not** multiply by internalization twice in utility.
- **Counter-based RNG:** keyed deterministic random choices; never create a global cross-agent random stream in the final core.

## Lifecycle & performance strategy

Phase 1: formal entities, external observation and action boundaries, memory/evidence ancestry, Noisy-OR and decision contracts in the headless core.

Phase 2: 20–50 NPC scenario with divergent beliefs, permissions, rumor propagation and explainable outcomes. Compare interventions (same seed; change visibility, consent or social norm). Count observed behavior changes rather than asserting human realism.

Phase 3: measure CPU time, allocations, decision-mode percentages and replay hashes for 50, 100 and 500 agents on a documented machine; only optimize bottlenecks. Use event-driven scheduling and lazy updates. The '90% automatic' figure from the design notes is **a hypothesis**, not a verified capacity.

Phase 4: Godot adapter, persistence and optional LLM advisor outside the critical frame/tick loop. Separate cheap undead/swarm simulation from high-detail cognitive agents.

## Verification status, 2026-10-11

**Implemented and checked in CI (.NET 10):**
- exact Noisy-OR (2- and 8-cause cases); the audited two-cause posteriors reproduce to tolerance;
- 5,000 seeded two-cause explaining-away/property cases;
- leak vs no-listed posterior distinction;
- provenance de-duplication for identical-root/claim evidence;
- stable softmax for extreme scores and deterministic zero-temperature tie-break;
- keyed RNG same-input reproducibility; parameter validation.

**Browser demo:** repeat root reports previously amplified evidence and created rumor echoes. Fixed by deduplicating received and forwarded root claims; regression tests included.

**Still unverified:** full C# actor/world separation at runtime, full semantic/psychology pipeline, realistic social emergence, true cognitive LOD scheduling, stable cross-machine replay, useful throughput at 500 NPC, full Godot integration. In particular, the mathematical tests are **not** an end-to-end architecture validation.

## Release gates for a credible public v0.2

1. Unit + property suite green across core and demo.
2. Typed World/Observation/Proposal contracts with runtime rejection of unauthorized mutations.
3. Hidden-knowledge differential tests: alter an unseen world fact and demonstrate the NPC decision trace stays unchanged.
4. Replay traces hashed across repeated equal-input runs (same runtime); counterfactual runs intentionally diverge only after intervention.
5. 20–50 NPC headless scenario with provenance-aware rumors and scenario-independent norm evaluation.
6. Performance report stating hardware, .NET version, measurements and variance.

## Explicit tradeoffs and risks

- Strict isolation adds adapter complexity but prevents omniscient NPCs.
- Exact Noisy-OR is tractable at eight causes but incompatible with arbitrary dependency graphs without a different inference method.
- Correlated reports can still double-count when the *same underlying source* has different IDs or superficially different content; root normalization is necessary but not sufficient.
- Full histories and traces can dominate memory; retain bounded runtime state, while the test harness archives diagnostic traces.
- C#/.NET is appropriate for desktop Godot, but **Godot 4 C# cannot currently export games to the web**. The browser demo remains JavaScript; it is not a transpilation of the C# core.

## Source notes

- [Microsoft .NET 10 lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
- [Godot 4 C# platform support](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html).
- Local project source: NOEMYN Cognitive PoC v0.1.1 mathematical-audit document (2026-10-09).

# ADR-0003 — Data-Driven, Modular and Extensible ENORYS

**Status:** Proposed architecture requirement; **not yet an implemented plugin SDK**.  
**Date:** 2026-10-12  
**Scope:** ENORYS Core, Affective Cognitive Map (ACM), game domains, persistence, third-party extensions.  
**Supersedes:** Any assumption that the list of needs, emotions, items, actions or social norms is hard-coded in the cognitive engine.  
**Related:** [ADR-0001 (headless kernel)](ADR-0001-ARCHITECTURE.md), [ADR-0002 (ACM)](ADR-0002-AFFECTIVE-COGNITIVE-MAP.md).

## Non-negotiable outcome

ENORYS must support installing **new data and new behavior modules without editing or rebuilding its Core**. A game must be able to include only the modules it needs. The Core should not contain switch statements on game-specific item names, emotion names, scripted story names, entity identities or mechanics. It is a cognitive runtime, not the SimWorld ruleset.

Three extension tiers:

| Extension tier | Typical user changes | Interface | Requires recompiling Core? |
| --- | --- | --- | --- |
| **Data Pack** | Needs, objects, actions, afflictions, emotion labels, norms, items, recipes, source affordances | Validated, namespaced and versioned JSON schemas | **No** |
| **Behavior Module** | Appraisal algorithms, memory, reasoning, social norms, goal generators, planning heuristics, new decision policies | \`Enorys.Abstractions\` versioned .NET interfaces + explicit capability grants | **No** |
| **World Adapter** | Game engine, sensors, action execution, simulation clock, renderer | \`IWorldAdapter\` contract with filtered observations and world-side action validation | **No** |

A \`Data Pack\` must not run arbitrary executable code. A behavior plugin may register computations, but not directly mutate the authoritative world. World adapters are a trusted host boundary and must be configured explicitly.

## Recommended assembly/package layout

\`\`\`text
Enorys.Abstractions         ← independent public SDK: IDs, packets, events, contracts
Enorys.Core                 ← orchestration, validation, per-agent state, deterministic runtime
Enorys.Epistemics           ← evidence ledger, Bayesian inference, belief revision [optional]
Enorys.Memory               ← episodic & semantic memory [optional]
Enorys.Affect.ACM           ← appraisal and emotional graph [optional]
Enorys.Goals                ← goal manager and bounded planner [optional]
Enorys.Social               ← trust, reputation, rumors, obligations [optional]
Enorys.Domains.Survival     ← survival objects, needs and actions [external domain pack]
Enorys.Adapters.Godot       ← game-world adapter [optional]
ThirdParty.MyExtension      ← custom compiled plugin [example]
data-packs/...              ← content and authored rules [example]
\`\`\`

These are **planned separations**, not existing packages; do not create empty package scaffolds merely to show this tree. Start with modular namespaces/projects and split into binaries when cross-module contracts stabilize.

The reference JS sandbox (\`src/v04\`) can remain a demonstrator; its exact internal JavaScript interfaces are **not** the stable SDK.

## Plugin contract: capabilities, registration, versioning

Proposed *illustrative* API sketch — **not compiled or released**:

\`\`\`csharp
public interface IEnorysModule
{
    ModuleDescriptor Descriptor { get; }          // namespace, version, API range
    void Register(IEnorysRegistry registry);     // registrations only; no world mutation
}

public interface IEnorysRegistry
{
    void AddAppraisal(IAppraisalProvider provider);
    void AddGoalSource(IGoalSource provider);
    void AddDecisionPolicy(IDecisionPolicy provider);
    void AddDataDefinition(IDataDefinition definition);
    void AddObservationProcessor(IObservationProcessor processor);
}

public interface IAppraisalProvider
{
    AppraisalResult Evaluate(in PrivateAgentView agent,
                             in ObservationPacket observation,
                             IScopedRandom rng);
}
\`\`\`

The API above is **illustrative**, so implementations must refine types, cancellation/budgets, deterministic ordering, allocations and ownership rules *before* considering it an actual SDK.

Each module manifest declares:
- globally unique, stable **namespace** (e.g. \`org.example.wilderness\`), version and host API compatibility range;
- dependency list, optional dependencies and conflict constraints;
- capabilities: \`ReadPrivateObservations\`, \`ReadAgentMemory\`, \`ProposeActions\`, \`RegisterDomainDefinitions\`, \`WriteNamespacedAgentState\`, etc.;
- exposed definition IDs and schema versions;
- deterministic behavior declaration and state snapshot/migration strategy;
- optional CPU/step budgets and timeout policy.

The registry checks for duplicate IDs, incompatible API versions, dependency cycles, unsupported hook combinations and requests for forbidden capabilities. Reject invalid configurations **before simulation starts**. Modules should default to reading immutable views; persistent writes are confined to their own namespaced state through a validated commit interface.

**Contract stability:** semantic versioning applies to \`Enorys.Abstractions\`, module manifests and save formats separately. Backward compatibility must be tested with older packs. Core internal structures stay private.

## Data Packs: data first, user extensibility without code

Suggested versioned JSON authoring format:

\`\`\`json
{
  "schemaVersion": "1.0",
  "namespace": "org.example.wilderness",
  "id": "org.example.wilderness:basic-survival",
  "version": "0.1.0",
  "dependencies": ["enorys.base >=0.5.0"],
  "needs": [
    {
      "id": "org.example.wilderness:hydration",
      "range": [0, 100],
      "initial": 25,
      "perDayDelta": 12,
      "meaning": "higher-is-worse"
    }
  ],
  "items": [
    {
      "id": "org.example.wilderness:clean-water",
      "tags": ["liquid", "drinkable"],
      "properties": { "hydration": 40, "contamination": 0 }
    }
  ],
  "actions": [
    {
      "id": "org.example.wilderness:drink-clean-water",
      "requires": { "inventory": { "org.example.wilderness:clean-water": 1 } },
      "effects": {
        "consume": { "org.example.wilderness:clean-water": 1 },
        "needsDelta": { "org.example.wilderness:hydration": -40 }
      }
    }
  ]
}
\`\`\`

The JSON is a **proposed schema example**, not executable against current ENORYS. We must first implement schema validation, semantic checks and an action-effect interpreter, and reject unknown/unsafe effect types. Authoring format can later be JSON, YAML or an editor; all normalize to a single canonical internal representation.

**No predefined universal emotions or needs in the Core.** IDs are namespaced strings at content boundaries and resolved to compact runtime symbols/integers for hot paths. Stable content identifiers must survive save/load and migrations; do not rely on registration order as permanent IDs.

### Object instances

The domain defines *item kinds*; the World Adapter owns individual *item instances* with a stable object ID, count/quantity, ownership, location, durability, contamination and custom typed properties. An inventory is a view over object instances and ownership relations, not a universal integer array of hard-coded item names.

### Custom fields and validation

User-added properties must declare a type, range/default, serialization version, authority/visibility policy and optional units. Protect against accidentally exposing an entity's global truth or private state to unauthorized agents.

## Events, ordering and conflicts

Modules hook into a **fixed ordered pipeline**:

1. World Adapter emits objective event and per-observer filtered packets.
2. Memory and epistemic modules update that observer's subjective state.
3. Semantic interpretation produces agent-local evaluations.
4. Appraisal/ACM and other goal sources supply influences.
5. Decision module proposes an action within a bounded budget.
6. World Adapter validates and atomically applies the proposal.
7. Verified results produce new observations and journal entries.

Rules:
- A module subscribes to **typed events** and phases; does not invoke downstream modules arbitrarily.
- Registry produces a deterministic topological order from declared dependencies, phase and stable ID; explicit conflicting writers are rejected.
- All environment/world effects are **data proposals** executed by the World Adapter after legal-precondition checks. A module cannot create resources by modifying a world object itself.
- Events are immutable and have stable IDs; multiple handlers may read one event, but writes must be committed at phase boundaries.
- Declare idempotence and anti-echo provenance to avoid duplicated emotional impulses, rumors or rewards on replay.
- Faulted modules have explicit error policy (abort deterministic run / disable and invalidate replay), rather than silently produce inconsistent state.

## Cognitive emotion extension (amends ADR-0002)

\`Enorys.Affect.ACM\` is a **replaceable Behavior Module**. It consumes generic \`Appraisal\`, agent-local evidence, semantics and target IDs. It emits \`AffectiveEpisode\` and utility contributions; never directly changes world facts.

The Core does not embed an \`enum EmotionKind { Anger, Fear, ... }\`. Emotions are registered by namespaced \`EmotionDefinitionId\` data plus optional calculation providers. A pack may define \`org.example.emotions:envy\`, \`...:relief\` or nonhuman/robotic state names without recompiling Core.

If ACM is absent:
- agent observation and beliefs still work;
- other independent modules may supply goal evaluation and decisions;
- no dummy emotions are generated to satisfy assumptions.

A user may replace ACM or add an appraisal provider; the composition rules specify **single writer** for owned emotional state plus optional additive read-only contributions. Two plugins cannot both mutate the same emotional episode without an explicit merger policy.

## Runtime performance and security boundaries

- Only event-affected modules should execute; avoid an unconditional \`NPC × plugins × edges\` scan.
- Use indexed subscriptions, typed compact IDs, bounded per-agent budgets, lazy affect decay, immutable views and measured SoA optimizations.
- Reproducibility is a **conditional contract**, assuming same module versions, seeds, inputs, build/runtime and host policies. Non-deterministic modules must declare themselves and invalidate strict replay claims.
- .NET \`AssemblyLoadContext\` + \`AssemblyDependencyResolver\` can load **trusted** plugins with separate managed dependency graphs (Microsoft tutorial), but **does not sandbox hostile code**.
- Third-party **untrusted** executable modules must run out of process behind an RPC boundary with OS-level restrictions / containers; this requires distinct design and performance testing.
- Prevent arbitrary file/network access by giving a trusted in-process plugin no such grants in the *public ENORYS API*, but do not pretend .NET interface restrictions alone are a security barrier.
- Data Packs are validated declaratively and do not execute arbitrary scripts.

References:
- Microsoft Learn: [Create a .NET application with plugins](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support).
- Microsoft Learn: [Dependency loading in .NET](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/overview).

## Proposed acceptance gates — **tests first**

1. Add a new user-defined need/item/action from an external JSON pack and simulate it **without changing or rebuilding Core**.
2. Load **two packs** with overlapping short names but distinct namespaces; no ID collisions.
3. Reject unknown effects, invalid ranges, duplicate IDs, missing dependencies and cyclic module dependencies before startup.
4. Load/remove ACM without breaking needs, planning and basic world actions; optional hooks degrade predictably.
5. Plug in **two different** appraisal algorithms; same private input changes emotion trace but never grants direct world mutation.
6. Change hidden world facts only; private agent decision and emotion traces must stay identical until filtered observations differ.
7. Block a plugin that attempts unauthorized world changes; no partial mutation or resource-ledger discrepancy.
8. Replay from snapshot with identical module/data versions and inputs; trace hash remains identical.
9. Upgrade content pack / module with a declared schema migration; snapshot loads without silent property loss, or loudly rejects unsupported migration.
10. Stress-test 50, 100, 500 NPC with 0, 5 and 20 registered **inert** and active modules; publish per-module time, allocations and decision-mode counts.
11. Demonstrate an external C# plugin implementing only published \`Enorys.Abstractions\`; no source changes in Core.
12. Validate **untrusted plugin isolation separately** before claiming the system supports arbitrary untrusted third-party code.

**No v1.0 public-SDK label until gates 1–11 pass with runnable examples and API compatibility tests.**

## Revised release order

- **v0.5:** public ID/packet/proposal/registration abstractions, data-pack loader+schema validator, external custom-need test, basic world authority in .NET.
- **v0.6:** modular memory/beliefs/ACM/goal composition; end-to-end self-contained .NET test and configurable survival domain.
- **v0.7:** deterministic plugin scheduler, per-agent state stores, snapshot and migration, rigorous 50–500-agent modularity/performance profiling.
- **v0.8:** independent Godot adapter; at least one wholly separate sample domain or alternate appraisal implementation.
- **v0.9:** module tooling / authoring editor, richer social behavior, graph visualization and reproducible third-party extension gallery.
- **v1.0:** stable modular SDK, versioned content schema, documented API, templates, clear trust model, CI compatibility matrix and reference benchmarks.

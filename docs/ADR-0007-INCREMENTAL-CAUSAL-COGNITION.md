# ADR-0007 — Actor-private incremental causal cognition

**Date:** 2026-10-12  
**Status:** Experimental module implemented and integrated into the ENORYS .NET 10 engine, opt-in.  
**Decision:** Preserve separate World Authority, AgentMind, ACM and planning modules while introducing a bounded **actor-private provenance DAG** that links observations, beliefs, emotional appraisals, motivational goal signals and relevant executed decisions.

## Why

ENORYS previously held private evidence (`AgentMind`) and emotions (`ACM`) as separate stores. The engine could choose actions using those states but had no stable mechanism to answer **why** a specific event changed an agent's motivation or action, or to invalidate only the affected derived states after a retraction. The missing layer is *causal provenance*, not a faster programming language.

## Contracts

- `ICausalGraphModule` in `Enorys.Abstractions`: `Observe(privateAgent, observation, privateAffect, cognitiveTick)`, `ActionBias`, `RecordDecision`, `Inspect`, `Explain`.
- `IModuleRegistry.AddCausalGraph`: multiple independent trusted C# modules may be registered; duplicate IDs are rejected.
- New standalone `src/Enorys.Cognition.Graph/` project depends **only on Abstractions**, not survival gameplay, Godot, the planner implementation, or ACM's concrete class.
- `Engine` invokes graph updates **after** filtered observations are ingested into `AgentMind` and optional ACM has appraised them; the graph never receives `SurvivalWorld`, inventory ledger authority, globally hidden incidents, or other agents' memories.
- `SurvivalBehavior` receives a bounded graph-derived utility contribution through `IDecisionContext.CausalBias`, in addition to existing emotions. A mathematical action certificate abstains when it cannot account for installed graph modules; legacy fast LOD does likewise.
- Without the graph plugin, old behavior is unchanged.

## Data model and dependency ordering

Each actor owns disjoint `CausalNode` and `CausalEdge` collections.

```text
private Observation (source root)
       |
    private Belief -------- AgentMind private evidence / trust
       |
 [optional Affect] ------- ACM private episodes, same source root/target
       |
  Motivational Goal ------ JSON-configured action-utility bias
       |
  Decision (if matching) - after World Authority action acceptance/rejection
```

All nodes contain agent ID, root, kind/stage, subject, strength, active status and cognitive tick. Edges encode which earlier state supports a later state. The `CausalRulePack` defines event type, subject source (`suspect` or `from`), target action kind, whether evidence must exist in private memory, optional ACM emotion ID, gain and decay.

**User-extensibility:** load more named rule packs (e.g. `data-packs/causal-custom.json`) or register an entirely new `ICausalGraphModule` via an external C# assembly. There are no hard-coded survival actions in the Core causal interfaces. Rules are strictly validated for IDs, duplicates, bounds, subjects, gains and decay.

The implemented stage graph is a **fixed acyclic pipeline** with configurable rules. It does not yet support arbitrary user-defined cyclic causal inference, dynamic schema migrations or arbitrary new node kinds.

## Invalidation and anti-echo

- Observation source-root identity is used to prevent the same forwarded claim from becoming a new independent reason whenever it is repeated. Repeated roots during the retained period increment `Unchanged` rather than reappraising the graph.
- Retractions deactivate derived belief/affect/goal nodes **only** if that actor's own `AgentMind` has already accepted a matching sender-authorized retraction. World truth alone does not change private beliefs.
- A decision that happened before a retraction remains part of history; its causal support can be viewed in the inspector as inactive. A current unsupported motivational goal contributes no further utility.
- Clock progression uses lazy exponential goal decay; an unchanged actor does not rebuild the entire DAG each turn.
- Bounded per-actor root/decision retention limits are configured by data packs, currently 96 roots and 96 referenced decisions for the base survival pack. Eviction removes retained incident subgraphs and dangling edges.
- **Limit:** once a source root is evicted, a much later repetition cannot be identified using the bounded in-memory anti-echo index. Durable provenance/tombstones and snapshot persistence belong to stage 13.

## Decision Inspector

`Inspect(agentId)` gives deterministic immutable copies of the actor's retained nodes/edges. `Explain(agentId, decisionNodeId)` traverses causal parents back to the observed source and indicates whether the chain reaches an observation. It refuses to explain a decision owned by another agent or one outside the retained graph.

These are **privileged diagnostic methods**, not capabilities granted to NPCs to inspect arbitrary minds. Trusted in-process C# modules are not a sandbox for untrusted code.

## Correctness invariants / release criteria

1. Observation→belief→optional affect→goal→decision explain paths are acyclic, actor-scoped and source-root grounded.
2. Duplicate messages from the same source root don't cause independent appraisals while the root is retained.
3. An unauthorized retraction does nothing; an authorized one invalidates only that root's dependent *current* states, never erasing accepted historical world actions.
4. Custom rule packs can add goal/action mappings without changes to Core.
5. Read-only graph mode (`biasScale=0`) exactly reproduces original action histories in tested worlds.
6. Active mode affects some near-tie decisions and produces deterministic replay; no claim of objective human realism.
7. Standalone and real-world tests enforce material ledger, actor isolation, deterministic action traces and bounded memory.
8. **Performance limitation:** causal graphs add computation and allocations, especially under dense social messaging. They are optional while further optimizations are developed.

## Follow-up

- Stage 12: contradictory belief revision, personality change and norm/relationship dynamics over many days; event-driven aggregation of related provenance roots.
- Stage 13: snapshot/restore of full graph, ACM and ActorMind with versioned data packs and durable source-tombstones; compaction.
- Stage 14: Godot `WorldAdapter` and interactive Decision Inspector for a 20–50 NPC narrative demo.
- Performance: cache action bias by revision/tick, index incoming edges for large graphs, compress repeated social observations and profile GC / peak RSS.

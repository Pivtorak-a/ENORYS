# ENORYS public sandbox: architecture and limits

This browser demo is deliberately smaller than the NOEMYN Cognitive PoC v0.1.1 mathematical-audit specification. The source specification targets **C# / .NET**, struct-based data layout, probabilistic belief updating, social semantics, emotions and a headless runtime. This JavaScript demo is an **independent thin slice** demonstrating selected conceptual invariants, not an implementation that meets those performance or mathematical requirements.

## Data flow

1. `createWorld` constructs seeded agents and the authoritative shared cache.
2. `step` selects the next agent in fixed round-robin order. It creates a local `perception` containing only the visibly accessible cache and nearby agent IDs.
3. `decide` receives only the selected agent's internal state and that `perception`, scores legal candidate actions using trait-weighted utility terms, and proposes the maximum-utility action.
4. `apply` is the **only** routine allowed to change supplies, central cache and the authoritative event log. It rejects impossible proposals.
5. Perception delivery turns actions into witnessed memories using a seeded random draw per nearby potential witness.
6. A `tell` action carries a claim from an agent's existing memory; recipients store it as **hearsay** (confidence 0.55), not as an eyewitness observation. The root event ID and immediate parent report support a provenance chain.
7. `Decision Inspector` reports utility terms, candidate totals and the actor's visible context. The UI, unlike agents, can inspect the omniscient audit log.

## Guarantees covered by automated tests

- A fixed seed, agent count, witness rate and steps produce structurally identical snapshots.
- All supplies remain nonnegative and supplies are conserved across the cache and agents.
- Agents never access the global event log in the decision function.
- An agent with no witnessed taking at witness rate 0 cannot invent a report.
- Hearsay always points to a report ancestry whose root exists in the authoritative audit log.
- Decision candidate scores exactly equal the sum of their displayed utility terms.

## Deliberate simplifications

- **No physical causality:** three abstract zones, no movement, distance, sensory occlusion, or complex consent model. Cache availability is publicly visible.
- **No full belief graph:** discrete eyewitness and hearsay memories with fixed confidence; no log-odds or Noisy-OR inference.
- **No general norms:** agent-specific norm weight affects taking and donating.
- **No social learning:** traits remain constant; only relationship scores and memories change.
- **No action sampling:** utility argmax with deterministic tie-breaking, not softmax.
- **No independent economic model:** finite supplies circulate; there is no consumption or replenishment except transfers.
- **No calibrated psychology:** generated agents do not demonstrate human validity.

## Next milestones

1. Publish and validate the narrower formal scenario suite from the audited specification.
2. Implement the headless C# kernel with stable IDs, testable facts/observations, utility vectors, and deterministic counter-based RNG.
3. Add tests for hidden information, semantics, belief provenance and counterfactual interventions.
4. Profile agent counts and CPU/memory before making performance claims.

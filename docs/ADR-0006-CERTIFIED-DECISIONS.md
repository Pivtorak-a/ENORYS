# ADR-0006 — Exact action-choice certificates, safe fallback and indexed private memory

**Date:** 2026-10-12  
**Status:** Experimental and opt-in in the ENORYS .NET 10 runtime; baseline remains default.  
**Implementation:** `Enorys.Abstractions.ICertifiedDecisionModule`, `Enorys.Domains.Survival.SurvivalCertificate`, `EngineOptions.CertifiedDecisions`, `EngineOptions.AuditCertificates`.  
**Validation:** [Experiment 010](EXPERIMENT-010-CERTIFIED-DECISIONS.md).

## Problem

In the previous [Experiment 009](EXPERIMENT-009-HYBRID-SURVIVAL.md), PulseGraph saved cognition work by **omitting some actors' decisions**. That changes food/water access and is an invalid optimization when identical action history and survival are required.

To accelerate cognition *without changing actions*, certify one action's superiority **for the current, complete and precisely defined decision input**. The world still commits an action on **every** nominal actor turn. Never infer from the current certificate that future turns can be skipped.

## Mathematical condition

Let `C_t` be the full set of feasible proposals the registered decision module would consider at turn `t`. Let `U(c,t)` be the *same* double-precision score used by its reference algorithm.

A certificate is accepted only when:

1. All proposal-producing dependencies are either evaluated by the proof or provably inactive.
2. Exact candidate feasibility and utility expressions are reproduced in the **same deterministic order** as the reference module.
3. A strictly unique winner is established:

```text
c* = argmax_{c ∈ C_t} U(c,t)
gap = U(c*,t) − max_{c != c*} U(c,t)
accept only when gap > 1e−10 and all values are finite
```

A tie or insufficient margin abstains. The algorithm returns only the winner's `ActionProposal` and proof metadata; it **does not construct the intermediate list of candidate records**.

For the survival example, the proof has access to current agent inventory, nine needs, health, traits, known supplies and threshold-gated proposals. It conservatively **abstains** if:

- An existing plan or *any potentially activatable* planning goal could introduce a proposal;
- A private social observation, request, obligation, correction or viable request could introduce a social proposal;
- An installed affective module does not explicitly certify that its influence is neutral;
- The result has a tie, near tie, non-finite value or unknown dependency.

The `IAffectNeutrality` contract is **opt-in and fail closed**: absence of implementation in any affect plugin forces full deliberation. `AffectiveMap` only attests neutrality if the actor has **no emotion episodes at all**.

These proofs apply to **one actor turn only**. Item consumption or a day boundary changes subsequent inputs; the proof must be recomputed. **No claim of a provably safe multiround inactivity certificate is made.**

## Independent validation

An optional `AuditCertificates` mode calls the **ordinary full reference `Choose` algorithm** whenever the fast proof succeeds, then checks the exact action proposal, candidate count and numerical score. A mismatch throws an error; **it cannot silently approve a changed decision**.

`ICertifiedDecisionModule` is implemented by the trusted survival module. External modules require their own independently reviewed proof; this is not an untrusted-code security sandbox.

Default flags:

```csharp
new EngineOptions(
    CertifiedDecisions: false,
    AuditCertificates: false,
    AdaptiveCertificateAdmission: false);
```

The feature is opt-in. To audit a reproducible scenario, enable both `CertifiedDecisions` and `AuditCertificates`. For performance measurement use `AuditCertificates:false` *only after tests validate the registered module*.

## Adaptive proof admission — without skipped decisions

If enabled, `AdaptiveCertificateAdmission` counts **proof abstentions per actor**. After three consecutive failures, future proof attempts are suppressed for three actor turns, then retried; the ordinary full decision algorithm is used on each suppressed turn. A successful proof resets the failure count.

This is a performance heuristic for deciding **when to try an optimization**, not a heuristic for whether a character should act. The certified and uncertified executions have the same semantics.

## Indexed episodic/epistemic evidence

Previously `SurvivalBehavior.FindSuspicion` repeatedly grouped and sorted all of one agent's evidence on every cognitive turn. The agent now constructs `IndexedEvidenceGroup` collections when an incoming message invalidates its private snapshot. Subsequent turns reuse a deterministically ordered, privately scoped grouping by incident and suspect.

- Maintain *original insertion-order group traversal* and descending log-likelihood/tie-root sort.
- Do **not** expose truth from `SurvivalWorld` or cross-agent records.
- `AgentView` allows optional index properties while retaining the existing positional constructor; third-party agent views still use the original unindexed path.
- At each turn the Bayesian formula still re-evaluates time decay exactly. We **did not** freeze time-dependent belief posteriors.

## Next development plan

1. Profile score generation and indexed-memory behavior independently (on fixed hardware) and remove remaining per-candidate temporary allocations.
2. Introduce safe *future* action stability windows only if the module proves bounded input rates, action side-effects and dependency invalidations; unknown plugins must revert to per-turn decisions.
3. Add per-zone activation switching and timing-wheel improvements separately; do not confuse approximate world LOD with exact action certification.
4. Add explicit causal graph dependency indexing for ACM and source provenance with bounded retention, then save/load deterministic snapshots.
5. Integrate into Godot and measure simulation vs physics/frame costs; no game FPS claims from this headless experiment.

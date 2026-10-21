# Experiment 012 — conflicting private incident hypotheses (first increment)

**Status:** experimental, opt-in query; existing ENORYS decisions are unchanged.

## Motivation

An agent may hear different accounts of the same incident. Comparing each suspect
in isolation makes it easy to present incompatible high-confidence conclusions.
ENORYS already stores actor-private, source-rooted evidence and authorized
retractions. This increment adds a **bounded, explicit comparison** of alternative
hypotheses without accessing World Authority or another actor's private state.

## Implementation

`src/Enorys.Core/Social/IncidentBeliefEstimator.cs` exposes:

```csharp
IncidentBeliefComparison result =
    IncidentBeliefEstimator.Compare(actor.Evidence, incidentId, cognitiveTick);
```

It returns `UnknownShare` and alternatives with `Suspect`, `Share` and
`IndependentRoots`. The values sum to one, but **are not validated probabilities
of guilt** and must not be used as proof against a person.

- Only active evidence for the requested incident is read.
- Each source-root contributes at most once. Multiple copies of the same claim
  cannot amplify confidence; an internally contradictory root is excluded.
- A negative log-likelihood contribution decreases a candidate's score.
- Older contributions decay toward a fixed base weight (800-tick time scale).
- Each candidate has a configurable-in-code baseline log-weight
  `log(0.12/0.88)`; the explicit unknown hypothesis has log-weight zero.
- Log contributions are bounded to prevent a single extreme message from
  overflowing, then normalized using log-sum-exp arithmetic.
- Inputs are iterated in deterministic order where necessary.

### Preconditions and limitations

1. **Mutually exclusive suspects:** this query only makes sense when an incident
   is defined to have one suspect among the enumerated alternatives, or no
   listed suspect. It must not be applied to multi-actor events.
2. **Heuristic shares, not Bayesian posteriors:** prior weights, aging and
   source dependence are research choices, not a validated cognitive model.
   These shares are not a confidence calibration.
3. The estimator only compares previously observed private evidence. It does
   not ask the authoritative world who is actually responsible.
4. The method does **not** modify `AgentMind`, the causal graph or existing
   game choices. Future experiments may integrate an opt-in decision policy
   after additional parity, performance and quality checks.
5. Provenance-root duplication is bounded by caller-supplied private evidence;
   a broader long-term provenance store remains future work.

## Reproducible tests

```bash
dotnet run --project tests/Enorys.Integration -c Release -- test
```

The integration suite checks independent supporting and contradictory evidence,
repeated/ambiguous roots, authorized deactivations, time decay, normalized
alternatives, and the explicit unknown option. No runtime throughput or
human-belief realism claim is made for this stage.

## Next work

Add controlled multi-day studies of belief revision and trust dynamics,
then compare opt-in decisions with the unchanged baseline using fixed-seed
replays. Only adopt a new decision policy after measuring unintended effects.

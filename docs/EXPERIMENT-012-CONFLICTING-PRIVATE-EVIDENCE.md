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

## Multi-day private belief journal (second increment)

The optional `PrivateBeliefTimeline` records evidence-driven revisions to an
agent's mutually exclusive hypotheses as the simulation progresses. It reads
only the actor's filtered `AgentView.Evidence` and received messages, never
authoritative incident truth or another agent's memory.

Enable it through the normal engine configuration:

```csharp
var result = new Engine(world, domain, moduleHost,
    new EngineOptions(TrackPrivateBeliefs: true, MaxBeliefRevisionsPerAgent: 64))
    .Run();

foreach (var revision in result.BeliefRevisions) {
    Console.WriteLine($"{revision.Tick}: {revision.AgentId} — {revision.Incident}");
}
```

- Changes are recorded only after new or corrected **private evidence**.
  Duplicate rumors and unauthorized retractions do not generate revisions.
- Each revision holds an immutable point-in-time comparison and its tick;
  the normal simulation and NPC policy are untouched.
- The per-agent retention limit is 1–2048 (default 64); when an old incident
  has no retained revisions, its cached comparison signature is removed too.
- The research export includes actor IDs and must remain **privileged**. It is
  not safe to expose an omniscient view to ordinary NPCs.
- The same evidence can produce different heuristic weights as it ages,
  even without a revision event. The journal is **event-driven** rather than a
  complete time-series sampled at every turn; call `IncidentBeliefEstimator.Compare`
  explicitly when an arbitrary-tick estimate is needed.
- Disabled by default to avoid memory and CPU overhead in large simulations.

The integration suite includes a multi-day contradictory-witness scenario,
repeated source-root delivery, unauthorized and authorized retractions,
retention pruning, and a seeded runtime replay demonstrating that enabling
the research journal **does not change decisions or world events**.

The journal is a diagnostic instrument, **not a validated learning algorithm**
or a claim that the agent has acquired human psychology.

## Opt-in social action policy (third increment)

A domain-specific policy in `src/Enorys.Domains.Survival/CompetingBeliefPolicy.cs`
uses `IncidentBeliefEstimator.Compare` to propose actions under conflicting
private testimony. The old policy remains the default and is unchanged.

Enable the experimental policy on the engine:

```csharp
var run = new Engine(world, domain, host,
    new EngineOptions(CompetingBeliefPolicy: true)).Run();
```

For a requested incident, an actor only considers its **own active evidence**
and an explicit unknown/other option. It can **investigate** after a plausible
claim, but an **accusation** needs (1) at least two independent provenance roots,
(2) heuristic support of at least 0.75, and (3) a lead of at least 0.35 over
both the unknown option and the next strongest alternative. Passing on a claim
has a separate support and margin gate. These numbers are **unvalidated design
thresholds**, not probabilities of real-world guilt.

The actor cannot read World Authority when choosing, and sender-authorized
retractions are reflected through the existing private `AgentMind` pipeline.
This is specifically a *mutually exclusive incident* policy. Multiple
co-perpetrator incidents are out of scope.

For compatibility with provable fast-path assumptions, both fast LOD and the
action-choice certificate abstain when opt-in social deliberation is enabled.
The full decision procedure then evaluates the new policy. This costs extra
CPU; benchmark before using it for very large numbers of NPCs.

### Five-seed controlled comparison

```bash
dotnet run --project tests/Enorys.Integration -c Release -- compare-beliefs
```

The command runs two identical scheduled-observation worlds (legacy vs
competing beliefs) for each of five seeds, prints their action hashes,
accepted accusations/investigations and accurate-accusation counts, and
asserts the resource ledger remains conserved. Differences are
**descriptive only**: five synthetic seeds do not prove improved intelligence,
realism, fairness, or performance. The direct regression tests additionally
check: one strong witness triggers inquiry instead of accusation; two
independent supporters permit accusation; contested alternatives suppress
premature blame; forwarded claims and retractions do not create fake support.

## Verified-outcome source trust (fourth increment)

Stage 12 includes an **experimental, opt-in source credibility update** for
the acting NPC, grounded in an authoritative, post-action verdict. The
existing survival-world `investigate` clue is deliberately *not*
considered verification because it has a known misidentification rate.
Ordinary witness messages cannot carry an authoritative verdict.

**Trust boundary:** only a trusted `IWorldAdapter.Execute` result may
attach a `VerifiedIncidentOutcome`. For the provided `SurvivalWorld`, this
feedback is emitted only after an accepted `survival:accuse` concerning a
real recorded world incident and only when `VerifiedAccusationFeedback`
is enabled. Unknown incident IDs, rejected actions and ordinary observations
never contain a verdict. The World Adapter knows the incident's objective
actor, but **only the accuser's private `AgentMind` gets the feedback**;
other NPCs do not receive world truth. This is a synthetic research oracle
in the survival benchmark, **not** a model of realistic investigation or
a decision that a real person is guilty.

Enable BOTH opt-in gates:

```csharp
var world = new SurvivalWorld(domain,
    new SurvivalConfig(Seed: 42, Agents: 24, Days: 14,
        VerifiedAccusationFeedback: true));
var run = new Engine(world, domain, host,
    new EngineOptions(AdaptiveSourceTrust: true)).Run();
Console.WriteLine(run.VerifiedSourceTrustUpdates);
```

On the first verified incident per actor, the engine compares the verdict
against that actor's *already received* active claims. Source ID is
`PrivateEvidence.Sender`, not the suspect. Each sender is evaluated at
most once for the incident, regardless of the number of forwarded roots.
Sources with incompatible active claims are skipped, as are withdrawn
claims and the acting agent's own reports. A matching source gains **+8**
trust units; an incorrect source loses **12** units, clamped to
`[-100, 100]`. These are design choices, **not learned calibration**.
The correction applies to subsequent incoming hearsay through the existing
`AgentMind.Ingest` reliability formula. Past belief values are intentionally
not rewritten, and the default policy remains unchanged unless other
experimental flags are independently enabled.

The same incident is processed only once, so repeated accusations cannot
farm reputation. The prior private trust snapshot stays immutable. The
integration regression suite exercises correct and incorrect sources,
duplicate roots, unauthorized feedback routes, unknown incidents,
retracted and contradictory reports, world/engine opt-out parity,
deterministic replay and future hearsay weighting.

### Boundaries

- This is supervised feedback from a simulated authoritative world.
  Different games or agent environments need an independently trustworthy
  verification mechanism; a text message, LLM answer or subjective clue
  must **not** be promoted to verified truth.
- This does not yet learn which *decisions* have good long-term consequences
  or change social trust based on unaudited success claims.
- Only an actual accepted accusation of an existing recorded incident can
  emit a verdict; an accusation without independent evidence could still
  be accepted by the example world. The cognitive trust mechanism cannot
  itself establish the justice of that game action.
- Per-actor processed-incident IDs are retained in memory for the lifetime
  of the engine instance. Persistence/snapshots and bounded long-term
  compaction need a separate follow-up before indefinite simulations.
- The synthetic tests prove correct *implementation of these rules*, not
  better accuracy or overall NPC intelligence.

## Next work

Add controlled multi-day studies of belief revision and trust dynamics,
then compare opt-in decisions with the unchanged baseline using fixed-seed
replays. Only adopt a new decision policy after measuring unintended effects.

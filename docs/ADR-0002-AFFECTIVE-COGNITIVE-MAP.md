# ADR-0002 — Affective Cognitive Map (ACM) for ENORYS

**Status:** Partially implemented experimental C# module (`src/Enorys.Affect.ACM`) as of 2026-10-12. The full semantic appraisal graph, persistence and psychological validation remain proposed. See [Experiment 005](EXPERIMENT-005-NET-SCHEDULER-ACM.md).
**Date:** 2026-10-12
**Context:** ENORYS v0.4 JS sandbox and the previously audited NOEMYN Cognitive PoC v0.1.1 specification.


> **Extensibility requirement:** ACM is a replaceable extension, not a mandatory built-in class. Emotion definitions use namespaced IDs and externally authored data packs, while third-party appraisal providers implement versioned interfaces. Core must not hard-code a closed list of `EmotionKind` values. See [ADR-0003 — Data-Driven, Modular and Extensible ENORYS](ADR-0003-EXTENSIBLE-MODULES.md). This design is proposed, not implemented.

## Decision

The **Affective Cognitive Map** is a first-class, domain-independent part of the cognitive engine, not five independent meters such as fear, anger or happiness. It represents **why an emotion exists**, **its target**, **what belief supports it**, **how it changes** and **which goals it affects**.

World events do **not** contain emotions. An agent forms emotions after subjective observation, belief update, semantic interpretation and appraisal. The same event can generate different (or coexisting) emotional responses depending on each agent's knowledge, identity, history and priorities.

## Causal cycle

\`\`\`text
WorldEvent (authoritative world)
  -> filtered ObservationPacket (per observer; may be incomplete or wrong)
  -> Episodic memory + Beliefs + provenance
  -> Semantic interpretation (ownership, consent, legitimacy, norms)
  -> AppraisalVector (harm, benefit, attribution, intent, violation,
                      surprise, control, future threat)
  -> Affective Cognitive Map (targeted episodes, feelings, associations)
  -> Goals + attention + bounded choice utilities
  -> ActionProposal (the world alone decides whether it succeeds)
  -> ActionResult + future private observations
\`\`\`

Affective consequences may change attention, memory retrieval, social valuation, threat estimation and goal priority. They do not overwrite objective truth or bypass world validation.

## Two coupled levels

### 1. Fast emotional state — current affect

A compact, continuously queried representation of active emotional episodes. Initial PoC emotions follow the audited five:
- anger (toward a hypothesized responsible party);
- fear (toward a threat);
- sadness (about a loss);
- gratitude (toward a perceived benefactor);
- guilt (about a perceived own violation).

*Extensions*, **only after tests and calibration**: joy, relief, shame, anxiety, hope, envy, resentment, affection, pride, humiliation, admiration. A larger vocabulary is not by itself deeper cognition.

### 2. Persistent emotional/semantic map — causal graph

Suggested node classes:

- \`Self\`, \`Person\`, \`Object\`, \`Place\`, \`Group\`;
- \`Goal\`, \`Need\`, \`Norm\`, \`Belief\`, \`Episode\`;
- \`Appraisal\`, \`AffectiveEpisode\`.

Suggested edges (typed, weighted and provenance-aware):

- \`BELIEVES\`, \`OBSERVED\`, \`HEARD_FROM\`, \`SUPPORTS\`;
- \`CAUSED_BY\`, \`ATTRIBUTED_TO\`, \`VIOLATES\`, \`THREATENS\`, \`BENEFITS\`;
- \`FEELS_TOWARD\`, \`ASSOCIATED_WITH\`, \`MODIFIES_GOAL\`, \`REVISES\`.

Only sparse relations relevant to the agent are kept hot; the entire graph does not need to be scanned per tick.

**Example:** A fears Person B for an apparent theft, remains grateful because B once helped them, and later acquires evidence that C was responsible. The new evidence changes attribution and can reduce anger toward B while guilt about accusing B and distrust toward the unreliable witness persist. Gratitude should *not* be deleted by the new information.

## Appraisal contract from NOEMYN v0.1.1

For subject NPC and source observation/beliefs, compute:

- \`harm ∈ [0,1]\` — perceived loss to a valued goal/interest;
- \`benefit ∈ [0,1]\` — perceived gain;
- \`causality(X) ∈ [0,1]\` — subjective P(X caused outcome);
- \`intent(X) ∈ [0,1]\` — subjective P(intentional | X, known evidence);
- \`normViolation ∈ [0,1]\` — believed norm violation, not an objective label;
- \`surprise ≥ 0\` — unexpectedness; normalize separately before affecting bounded emotion;
- \`control ∈ [0,1]\` — the agent's perceived ability to change outcome;
- \`futureThreat(X) ∈ [0,1]\` — expected future danger.

Do not merge causality, intention, legal exception, consent and moral blame into a single \`responsibility\` flag.

Starting heuristics from the audited specification, \`C_X=causality(X)\`, \`J_X=intent(X)\`, scaled by the agent's reactivity:

\`\`\`text
Anger(X)    = harm * (C_X*J_X) * (0.5 + 0.5*normViolation)
Fear(X)     = futureThreat(X) * (1-control)
Sadness     = harm * (1-control) * (1-max_X(C_X*J_X))
Gratitude(X)= benefit * C_X
Guilt       = normViolation_own * C_self * J_self
\`\`\`

These formulas are *working modeling assumptions*, not measured human psychological laws. In particular the original anger formula can miss anger at negligence; a separate model revision will test \`C_X*(b0+(1-b0)*J_X)\` against evidence.

### Numeric reappraisal example

Suppose \`severity=.8\`, \`belief(no consent)=.7\`, \`legitimacy=0\`, \`internalization=.9\`.

\`\`\`text
V_before = .8 * .7  * (1-0) * .9 = .504
V_after  = .8 * .05 * (1-0) * .9 = .036
\`\`\`

With fixed harm and attribution, the anger *amplification factor* from the formula changes \`0.752 -> 0.518\`, a **31.117%** reduction, NOT 50%. Updating beliefs must not guarantee immediate forgiveness or instantly delete an already present emotional state.

## Temporal dynamics

Each emotional episode is keyed by \`(agentId, subjectId, emotionKind, appraisalSourceId)\`. It stores:

\`\`\`text
AffectiveEpisode {
  emotionKind, targetId, sourceEpisodeId, appraisalRevisionId,
  intensityAtTime, observedAtTick, decayRate,
  beliefVersion, goalLinks[], provenanceRoots[], active
}
\`\`\`

Lazy decay:

\`\`\`text
I(t) = I0 * exp(-lambda*(t-t0)),  t >= t0
\`\`\`

New independent impulse of same emotion+target can combine with saturation:

\`\`\`text
Inew = 1 - (1-Iold)*(1-Iimpulse)
\`\`\`

**Important:** A re-read of the *same memory* or repeated rumor source is **not** an independent new impulse. Belief revisions apply controlled delta/reappraisal with traceable reasons; reappraisal must be idempotent for the same \`beliefVersion\`.

Do not force all emotions into a single positive/negative scalar: conflicting simultaneous emotions are valid. If a scalar summary is needed for inexpensive scheduling, treat it as a derived view, never the authoritative emotional memory.

## Integration boundaries

- **Memory/Epistemics:** provide subjective beliefs, source lineage, episode history and corrections.
- **Semantics:** evaluate the *agent's* interpretation of permission, property, norms, obligations, legitimacy.
- **Needs/Survival domain:** provide only typed utility impacts (loss/benefit/risk); no hard-coded anger for specific item names.
- **Personality:** empathy, impulsivity, suspicion, reactivity and other stable traits affect *how* appraisal translates into intensity and decisions; traits ≠ emotions.
- **Goal/Planner:** current affect adjusts goal priorities and perceived outcomes but action proposals still pass independent feasibility checks.
- **Runtime/LOD:** lazily update only affected target/episode edges; urgent new evidence wakes the agent for deliberation. Full graph sweeps every tick are disallowed.
- **Decision Inspector:** show \`observation -> belief -> semantic rule -> appraisal -> emotional episode -> utility change -> action\` and separate objective truth (developer-only) from agent perspective.

## Mandatory staged implementation

**v0.5 contracts:** \`AppraisalVector\`, \`EmotionKind\`, \`AffectiveEpisode\`, \`EmotionTarget\`, \`EmotionTrace\`, private graph access; deterministic, serializable schemas. The v0.5 world/agent boundary MUST be tested before behavioral adoption.

**v0.6 minimal ACM:** five baseline emotions, targeted episodes, lazy decay, explicit personality reactivity, reappraisal delta/idempotence, memory provenance, dynamic goal utility effects. Keep goals, norms and emotions separate but linked.

**v0.7 performance:** sparse indexed graph, attention-triggered reevaluation, target-based lazy updates, bounded memory budgets; benchmark alongside Cognitive LOD for 50, 100, 500 NPCs.

**v0.8–v0.9:** visual Godot Decision Inspector (not a Godot-dependent core), longer-horizon mixed emotions, friendship/resentment/grief, relationships and group social feedback, retraction and trust repair. LLM optional, never sole generator of emotion transitions.

**v1.0 gate:** published reproducible emotional scenario suite, signed decision provenance, benchmarks, robust schema migration and documented model limitations. Do not claim psychology validity absent external validation.

## Acceptance scenarios (test-first)

1. **Epistemic isolation:** change hidden truth without new observation; affect and decisions stay unchanged.
2. **Same event, different agents:** different norms/attachments/traits produce different emotion traces for the *same perceived event*.
3. **Multi-target, mixed emotions:** anger and gratitude toward one agent may coexist; neither overwrites the other.
4. **Reappraisal:** consent evidence lowers the computed norm-violation component; earlier stored emotion decays/reappraises by policy, not forced instant cancellation.
5. **Source anti-echo:** hearing the exact same rumor root through 10 intermediaries does not trigger 10 independent emotional impulses.
6. **Reversals/false accusations:** correction can create remorse/guilt and reduce blame; the system must keep original observation trace.
7. **Decay and boundedness:** decay is monotone without new impulses; all bounded intensities remain finite within [0,1]; negative time steps rejected.
8. **Reproducibility:** same seed, build and inputs produce identical canonical emotion/action traces.
9. **Performance:** emotional bookkeeping scales with updated sparse edges/events, not dense every-NPC to every-other-NPC iterations.
10. **Decision tracing:** the inspector can explain why appraisal affected a goal or action even if the final action did not change.

## Do not confuse proof levels

Mathematical invariants/seeded test outcomes establish that our implementation follows **our model**. They do not establish that the model faithfully describes human feelings, cognition or consciousness.

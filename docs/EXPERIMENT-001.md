# Experiment 001 — One event, many subjective beliefs

**Date:** 2026-10-11  
**Implementation:** `scripts/experiment-001.mjs` (JavaScript research harness; **not** the planned production C# runtime).  
**Replication:** `node scripts/experiment-001.mjs` and `npm test` (Node 20+).  
**Protocol:** 40 initialized NPC, one actor, one owner, **38 eligible observers**, fixed social ring, one ambiguous object transfer, seeded traits, fixed pseudo-random draws.

## Question

Can agent-local evidence about **the same world event** result in different beliefs and actions, without cognitive code branching on individual identities? The objective state may say the transfer was unauthorized, but agents **do not read the hidden permission field**. A visible suspicious cue is not absolute proof of consent or intent.

This is a mechanism test, not a demonstration of independent intelligence, human psychology, genuine emergent society, or a full ENORYS end-to-end engine.

## Parameters fixed *before* reading outcomes

- Prior P(no consent) = 0.2.
- Directly observed suspicious cue: likelihood ratio = 10.
- One hearsay report: effective likelihood ratio = 3^0.6; first report adds full log evidence.
- Distinct root reports beyond the first are discounted to 25% log-evidence (heuristic to reduce dependent evidence).
- Repeated receipt with the **same eyewitness root** is deduplicated.
- Norm intensity (individual): sampled uniformly in [.25, .95]. Norm severity = 0.8.
- Personal courage, curiosity and sociability: seeded pseudo-random values in [0,1).
- 1–2 direct-hop reports are transmitted in a fixed ring network; they do not become new eyewitnesses.
- Agents may: **ignore**, **investigate**, **confront**. This is a **single decision after the event**, not multi-step planning.
- Sensors sampled at p = 0, .35, 1 for counterfactual visibility; identical seeds yield identical agent traits and underlying draws.

### Analytical expectations

For 38 potential eyewitnesses and visibility probability p:

```text
W ~ Binomial(38, p)
E[W] = 38p
SD(W) = sqrt(38p(1-p))
SE(mean over 1000 seeds) = sqrt(38p(1-p)/1000)
```

At p=.35: E[W]=13.3; SD=2.9402; Monte Carlo mean expected within roughly **13.118–13.482** under a normal 95% approximation.

Evidence updating:

```text
P(no consent | evidence) = sigmoid(logit(.2) + sum(effective log-likelihood ratios))
No new evidence -> 0.200000
Single direct observation -> 0.714286
Single hearsay source -> 0.325826
```

Utility terms (toy model, chosen for this research harness, not empirically calibrated):

```text
V = 0.8 * posterior * NPC_norm
U(ignore)      = 0.50
U(investigate) = 1.3 * V + 0.4 * curiosity
U(confront)    = 2.0 * V + 0.35 * courage - 0.10
```

Investigation/confrontation are unavailable **when no evidence exists**. Highest-utility action wins, with deterministic tie-breaking. There is no independent mathematical prediction of the exact number of confrontations without integrating over these generated traits and communication graph — those are **observed simulated outcomes**.

## Actual results, seed=42 (one run)

| Witness visibility | Eyewitness | Hearsay-only | Uninformed | Ignore | Investigate | Confront | Mean posterior |
|---|---:|---:|---:|---:|---:|---:|---:|
| 0% | 0 | 0 | 38 | 38 | 0 | 0 | 0.200000 |
| 35% | 16 | 14 | 8 | 17 | 6 | 15 | 0.468141 |
| 100% | 38 | 0 | 0 | 4 | 7 | 27 | 0.755804 |

The 35% group counted 16 witnesses, comfortably within the expected variance of Binomial(38,.35). The increase from 13.3 expectation is ordinary sampling noise; it is **not** a model discovery.

Example: at 35%, A04 saw the cue and chose confront; A10 also saw the cue but chose ignore because its personality weighting yielded a different utility ordering. A07 heard about the event indirectly and ignored it. Their personal opinions need not equal objective truth.

## Monte Carlo: 1,000 seeds (1–1,000); group averages

| Visibility | Expected eyewitness E[W] | Measured eyewitness | Hearsay only | Uninformed | Ignore | Investigate | Confront | Mean posterior |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 0% | 0 | 0 | 0 | 38.000 | 38.000 | 0 | 0 | 0.200000 |
| 35% | **13.300** | **13.345** | 9.605 | 15.050 | 22.194 | 4.843 | 10.963 | 0.418493 |
| 100% | 38.000 | 38.000 | 0 | 0 | 3.457 | 7.784 | 26.759 | 0.753648 |

35% visibility eyewitness mean differed from the analytic expectation by **+0.045 NPC** (+0.34%). The default model showed increased confrontations as visibility increased, but this reflects the selected evidence weights, utilities, and social graph.

### Rumor counterfactual, same 35% visibility

- Single seed 42: **without** rumors 11 confrontations; **with** rumors 15.
- Across 1,000 seeds: **without** rumors 8.965 confrontations/run; **with** rumors 10.963 confrontations/run, a difference of **+1.998** (+22.29% relative).
- The witnesses themselves do not change between paired runs. More agents become informed through reports, not illicit access to the objective world state.

## Passed validations

1. Bayesian single-evidence posteriors match analytic values.
2. Same seed and inputs reproduce the trace.
3. 38 eligible agents are accounted for exactly in every scenario.
4. Changing hidden world permission without changing observed cues leaves **all decisions identical** (isolation boundary).
5. Root ID deduplication prevents forwarding one eyewitness report as multiple independent sources.
6. No eyewitness means no spontaneous evidence or accusation.
7. All eligible observers see the event at p=1.
8. A fixed seed increases or preserves the eyewitness set as p increases.
9. The 1,000-run witness count approaches Binomial expectation.
10. Different observers select different actions under identical objective world state.

## Critical limitations

**The Bayesian parameters, norms, social network, and utility coefficients were chosen by us.** Thus this experiment proves consistency of the toy algorithms and causal sensitivity to information, *not* realism. In particular:
- The direct cue is stipulated (it is not generated from a physical 3D sensory scene).
- A distinct eyewitness root does not necessarily imply independent evidence; the 25% factor is a heuristic.
- Some observer agents are more confrontational by construction. The simulation confirms logic, not human behavior.
- The actor and owner are excluded from the 38 observer reactions. No future adaptive feedback, coercion, deception inference, lasting dynamic goals, or multiple-event social simulation.
- Full C# observation/belief/world adapter integration, realistic behavior, and 50/500-agent throughput remain **unverified**.

**Next falsifiable test:** feed *different sequences of fallible perceptions and norms* into the future C# headless engine, and use a held-out set of interventions (including misleading reports and reversals of consent evidence). Compare behavior with independently specified expected invariants; publish failure cases as well as passes.

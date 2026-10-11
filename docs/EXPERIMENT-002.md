# ENORYS Experiment 002 — A history without a scripted plot

**Date:** 2026-10-11. **Status:** reproducible JS social simulation prototype, not yet part of the .NET 10 runtime and not a psychological validation.

## Hypothesis

A fixed set of domain actions, private knowledge, fallible perception, utility-based choices, and consequences will create multi-event histories without a prewritten sequence or explicit identity-specific story conditions.

Agents (default **24**) choose exactly one action per turn over **12 days** (288 decisions). There are ten candidate actions: forage, take, share, tell, investigate, accuse, defend, repay, guard, or wait. The scenario is a rationed community depot: the initial depot contains 20 rations, initial agent stocks are seeded. Every daily cycle agents consume one ration if available; starvation-days accrue otherwise. Foraging creates two rations, taking transfers two from the depot, gifts/restitution move supplies back into the depot. No external resource mutation happens inside `decide()`.

## Evidence and mathematics

- Before an eyewitness has any evidence, the default subjective prior for the hypothesis "X took restricted supplies in incident E" is `P(H)=0.12`.
- A visually observed, possibly mistaken identification has `LR=8` (logLR = 2.07944), producing posterior `0.521739` for that named suspect.
- Forwarded information retains the original eyewitness root, with a reduced log likelihood `0.55 * ln(8)`. Repeat receipt of the same root for one incident is discarded.
- Different root observations about the same suspect are **heuristically** discounted: after the first, each carries 25% of its log-likelihood. This is not a fully calibrated causal Bayesian network.
- Per possible witness, visibility is 30% (if no guard). The chance of mistaken identity conditional on having seen the incident is 10%. During an active guard action visibility rises by 20 percentage points and theft becomes less attractive via an explicit negative utility term.
- Decisions maximize the sum of displayed action utility terms. All traits/weights are experimental defaults, not empirical human behavioral estimates.

For a **single** theft that begins without an active guard, `W ~ Binomial(23,0.30)` because one actor cannot be a witness. Thus `E[W]=6.9` and `SD(W)=sqrt(23*0.3*0.7)=2.1977`. For `seed=42`, the first incident **E0006** has **7 direct witnesses**. This matches the expected count, not the realism of social responses.

## Actual story: seed=42, 24 agents, 12 days

One event-to-consequence chain reconstructed **after execution** from the real log (not hard-coded narrative):

| Day | Event | Actor | Result |
|---|---|---|---|
| 1 | `E0018` | Tania (`A18`) | Takes 2 restricted rations from the communal depot |
| 1 | `E0021` | Yaroslav (`A21`) | Tells Zlata that Tania was implicated |
| 2 | `E0028` | Danylo (`A04`) | Correctly accuses Tania concerning `E0018` |
| 2 | `E0035` | Lada (`A11`) | **Wrongly accuses Borys** (`A02`) of the *same* incident |
| 2 | `E0042` | Tania (`A18`) | Returns 2 rations after receiving social pressure |
| 3 | `E0050` | Borys (`A02`) | Publicly disputes the accusation against him |
| 4 | `E0076` | Danylo (`A04`) | Investigates the incident, finds a misleading new clue implicating a third person |
| 4 | `E0093` | Yaroslav (`A21`) | Investigates and finds a clue implicating Tania |

Why did Lada blame an innocent person? Her own direct perception of `E0018` was wrong: the eyewitness observation in her private record identified **Borys**, not the objective actor Tania. At `E0035`, Lada assigned Borys a posterior `P=0.521739`, and her utilities were:

| Candidate action | Decision utility |
|---|---:|
| Accuse | **1.5589** |
| Guard | 1.1281 |
| Tell | 1.1002 |
| Investigate | 0.8637 |
| Forage | 0.7024 |
| Wait | 0.2500 |

These scores are exactly what the simulation calculated for this choice. A more deliberate or risk-averse agent might have investigated instead. The objective truth is available in the narrator/debugger, not to Lada's decision routine.

Over the full 12-day run, the recorded totals are:

| Metric | Result |
|---|---:|
| Unauthorized depot removals | 16 |
| Public accusations | 26 |
| True / false accusations | 25 / 1 |
| Investigations | 18 |
| Private reports | 83 |
| Restitutions | 7 |
| Guard shifts started | 5 |
| Starvation-days | 17 |
| Supply-conservation residual | **0** |

## Counterfactuals

Paired runs use **identical seed=42** and initialized traits. Changing observations or information-sharing changes subsequent choices and therefore changes the world. These are **counterfactual simulations**, not randomized human experiments.

| Condition | Removals | Private reports | Accusations | Wrong accusations | Starvation-days |
|---|---:|---:|---:|---:|---:|
| 30% visibility, rumors enabled (baseline) | 16 | 83 | 26 | 1 | 17 |
| **0% visibility**, rumors enabled | 13 | 0 | 0 | 0 | 0 |
| 30% visibility, **rumors disabled** | 9 | 0 | 29 | 2 | 10 |
| 75% visibility, rumors enabled | 12 | 88 | 24 | 0 | 13 |

A zero-observation world produces no evidence, accusations or social investigation from unknown events. Rumor removal does **not** automatically stop public accusations; they are a different communication channel. This makes the contrast more realistic than equating all public speech with private gossip.

Across seeds **1–100** (100 independent initializations under the same rules), mean outcomes per 12-day run are:

| Visibility / gossip | Removals | Reports | Accusations | False accusations | Restitutions | Starvation-days |
|---|---:|---:|---:|---:|---:|---:|
| 30% / yes | 16.86 | 83.15 | 26.63 | 1.77 | 6.54 | 11.10 |
| 30% / no | 15.44 | 0 | 26.63 | 1.88 | 6.98 | 6.16 |
| 0% / yes | 17.51 | 0 | 0 | 0 | 0 | 0 |
| 75% / yes | 12.99 | 88.57 | 26.34 | 1.44 | 2.86 | 11.64 |

The mechanisms can produce social conflict and wrong accusations from limited subjective evidence. The exact frequencies depend **strongly** on parameters intentionally chosen by the author; they are not general claims about real societies. In fact, this run highlights a flaw worth investigating: excessive time spent on social actions can increase food deprivation. That is an outcome of our invented utility functions, not a validated emergent psychological phenomenon.

## Verification

Run:

```bash
node scripts/experiment-002.mjs 42 12
npm test
```

The integration tests cover replay determinism; no private decision changes when secret world truth is replaced; candidate-score decomposition; proposal purity; resource conservation for 80 seeds; empty-evidence controls; false-identification controls; distinct provenance roots; justified accusations; non-scripted full story reproduction; gossip/visibility counterfactuals; no self-accusation; valid parameters.

The interactive user-facing version is [`story.html`](../story.html), locally over `python3 -m http.server 8000` or via Pages when enabled.

## Limitations and future work

- This is a single shared settlement with abstract actions, 24 round-robin agent turns per day. No 3D sensing, complex world navigation, injuries or survival tactics.
- Investigation generates a probabilistic clue **conditioned on true history** through the world adapter. The agent sees only the resulting clue. We have not simulated persistent physical traces.
- `0.12` prior, likelihood ratios, traits, social graph, payoff weights, and mistaken-identity probabilities are hand-selected. Behavioral frequency and realism are **unvalidated**.
- Only sparse incident claims and one-turn proposals exist. No autonomous long-horizon goals or sophisticated belief-revision across evidence contradictions. An accusation may remain socially harmful even after restitution.
- No true dynamic group formation, deliberate deception, negotiation, self-reflection or sophisticated norm reasoning. Do not confuse the current action types with full social cognition.
- The current interactive history is a standalone JavaScript experimental harness, **not an integration test of the planned C# ENORYS kernel**. The next task is to move typed perception/event/proposal contracts into C#, maintain differential isolation tests, and compare traces.

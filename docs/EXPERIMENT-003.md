# ENORYS Experiment 003 — Needs, Items and Multi-Agent Survival

**Status:** Tested interactive JavaScript research prototype, 2026-10-11. **Not** the production .NET cognitive runtime. **Not** a scientifically validated model of human physiology or psychology.

## Reproduction

```bash
npm test
node scripts/experiment-003.mjs 42 12
node scripts/experiment-003-study.mjs > docs/experiment-003-results.json
python3 -m http.server 8000 # open /survival.html
```

The batch study uses 80 paired seeds, 24 agents and 12 days per scenario, with three actions per NPC-day: **864 scheduled decisions per run**. Counterfactuals use identical initial seeds and agent trait generation. The published `experiment-003-results.json` contains the complete result arrays in aggregated form, including paired approximate normal confidence intervals computed across seeds. These intervals describe **variation inside this simulator**, not uncertainty about human behavior.

## Model coverage

Nine dynamically varying deficits, each clamped to 0–100 (higher = worse): **hunger, thirst, fatigue, cold exposure, hygiene deficit, bladder/toileting, perceived lack of safety, social isolation, negative morale**. Additionally, health is 0–100 (higher = healthier), with injuries, infection, potentially fatal deterioration and resource-dependent care. This is a core survival subset, not a claim to exhaust every human need (e.g., oxygen deficiency, pregnancy, chronic illnesses, nutrition deficiencies and psychological meaning are outside scope).

Fourteen typed items/materials/objects: `food`, `water`, `raw_food`, `dirty_water`, `wood`, `medicine`, `bandage`, `soap`, `blanket`, `coat`, `tool`, `bed`, `shelter`, `latrine`. Each has a role; some are consumed, some are durable, and some are constructed infrastructure. The available actions include eating, drinking, emergency raw ingestion, sleep, toileting, warmth, hygiene, treatment, first aid, socializing, gathering, scavenging, cooking, boiling, building a shelter/latrine, asking for help, giving items, unauthorized taking, rumor transmission, investigation, accusation, defense, repayment and guarding.

**Mechanisms:** clean water improves hydration; dirty water can temporarily reduce thirst while increasing infection risk; raw food can relieve hunger at health risk; wood can become warmth, a shelter, a latrine, or fuel to purify water; injuries and infection increase health pressure; beds and clothing change future needs; asking for help can produce item transfers; witness uncertainty can cause false accusations. There are no character-specific scripted events.

## Formal properties

For each item type `k`, define `T_k(t)` as total units held by all agents + community depot + remaining source at time `t`. `C_k(t)` counts explicitly consumed units; `Δ_k(t)` counts signed conversions from/to `k`.

```
T_k(t) + C_k(t) − Δ_k(t) = T_k(0)
```

This is **exact integer inventory conservation**, including `raw_food → food`, `dirty_water + wood → water`, and `3 wood → shelter` or `2 wood → communal latrine`. It does **not** imply conservation of calories, matter or energy across item types. Model health/needs are dimensionless utility variables.

Expected invariants before testing: same seed and conditions produce identical event/decision traces; all nine needs and health stay between 0 and 100; the authority cannot give more items than it owns; a victim's private belief must derive from received observations; changing an unseen authoritative incident identity does not alter the prior private decision; repeated rumor roots do not add independent evidence; turning visibility off prevents eyewitness information.

## Results: seed 42, baseline

| Metric | Measured |
|---|---:|
| Living agents | 24 / 24 |
| Mean health (higher is better) | 90.186 |
| Conservation by item | 14 / 14 passed |
| Distinct action types | 25 |
| Items given directly | 23 |
| Requests for help | 44 |
| Unauthorized depot withdrawals | 11 |
| Correct / false accusations | 10 / 3 |
| Food preparation / water boiling | 44 / 59 |
| Hygiene / medicine / bandage | 17 / 2 / 1 |
| Shelter construction / latrine use | 3 / 112 |

For full per-scenario single-seed numbers, see [`experiment-003-results.json`](./experiment-003-results.json).

**Example unscripted chain in this run:** Danylo requests wood; Fedir and Iryna give supplies. Emma removes food from the common depot, Petro accuses her, but Marta erroneously accuses Petro of an incident. The suspect and the accused have distinct private evidence histories; reactions include denial and altered safety behavior. Meanwhile agents find food, boil water, use the latrine, bandage injuries, and build shelters. These are outputs of the chosen rules, **not evidence of human-like reasoning**.

## 80 paired seeds: mathematical sensitivity analysis

| Metric (mean per run) | Baseline | 35% resources | Severe weather | No witness/gossip |
|---|---:|---:|---:|---:|
| Mean health | 88.338 | 74.188 | 83.492 | 88.967 |
| Mean thirst deficit | 45.594 | 71.202 | 49.620 | 43.014 |
| Mean cold deficit | 35.177 | 44.823 | 47.793 | 32.776 |
| Direct transfers (`give`) | 23.750 | 16.913 | 27.475 | 25.063 |
| Scavenging (`scavenge`) | 82.150 | 215.250 | 86.287 | 87.412 |
| False accusations | 1.000 | 0.838 | 0.713 | 0.000 |

Paired scarcity effects: mean health **−14.150** (approximate 95% interval **[−16.011, −12.289]**), mean thirst deficit **+25.607** (**[23.841, 27.373]**), direct transfers **−6.838** (**[−7.863, −5.812]**). Severe-weather effects: mean health **−4.846** (**[−5.582, −4.109]**), cold deficit **+12.616** (**[11.190, 14.043]**). Model-generated variability only; no external empirical validation.

Results are descriptive; parameters and candidate utilities were tuned during model development and were **not pre-registered**, so these scenarios do not provide an independent test of human behavioral realism. Physiological and social interactions need more rigorous calibration and independent held-out scenarios.

## Verified checks

`npm test` runs all JS suites, including new tests for: 14-item conservation (150 runs), scarce winter invariants (75 longer runs), deterministic replay, private decision purity, hidden world truth isolation, smoke tests for treatment/first aid, dehydration and hunger responses, day tick behavior, rumor ancestry, scenario sensitivity, and event coverage. .NET core CI is a separate earlier suite; the new survival simulation is **not yet ported** to C#.

## Current known limitations / failure analysis

1. **Utility weights are hand-designed**; balancing action frequency is a simulation engineering choice, not a calibrated psychological response model.
2. Needs and deterioration use normalized units; diseases, injuries, food quality and social states are simplified. Oxygen, excretion beyond generic toileting, chronic illnesses and reproductive needs are not simulated.
3. World sources have finite item reserves but no geographical search time, carrying capacity or trading market. Weather is deterministic and coarse, not a climate model.
4. A witness can misidentify a person. Source-root deduplication prevents a single echo from multiplying into many evidence contributions; it does not prove independence of different sources.
5. Social requests can be rejected implicitly (agents choose other actions); long-term intentions, explicit promises and persistent contracts do not exist.
6. `decide` is read-only; `apply` handles the world. However the current JS runtime is a **single module** with in-memory state, not a strongly enforced typed multi-package boundary.
7. Chronic over-scavenging and resource exhaustion under scarcity show a design issue: agents currently lack explicit multi-action plans that combine gathering wood → boiling water → drinking. Some short-sighted choices waste scarce actions.
8. The C# engine remains separate math primitives; no claim is made about 500 NPC performance, Godot deployment or behavioral validity.

## Proposed independent follow-up tests

- Hide depot contents until an NPC physically inspects it; vary the hidden state without changing its decision until an observation arrives.
- Water contamination and shelter failure: verify first-order needs responses plus second-order disease and cooperation.
- Build plans with prerequisites and budgets; compare need recovery and wasted actions to the current reactive utility engine across fresh random seeds.
- Multiple incompatible clues and retractions: verify belief revision and evidence provenance over days, without special-case scripted identities.
- Profile exactly the same high-level behavior when ported to the C# headless runtime, then optimize hot data structures based on measured allocations and throughput.

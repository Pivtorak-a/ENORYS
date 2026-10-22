# Experiment 013 — Adaptive Experience Policy (second increment)

**Status:** research prototype, **off by default**. Extends
[Episodic Outcome Memory](EXPERIMENT-013-EPISODIC-OUTCOMES.md) with
an actor-private online table of bounded preferences learned from
**recorded immediate observations**, not from authoritative world truth.

## Pipeline

1. `Engine` asks the standard policy to choose among feasible candidates.
2. `SurvivalBehavior`, when the experimental mode is active, adds a
   bounded learned preference to eligible self-maintenance candidates.
3. The world executes exactly one action; normal feasibility checks still
   apply. After execution, `EpisodicOutcomeMemory.Record` captures the
   actor's own immediately observable health, average need pressure and
   inventory-unit change.
4. `AdaptiveExperiencePolicy.Learn` updates the actor's preference for
   the chosen action in its prior need-pressure context.
5. Later candidate scoring can use the updated preference, but
   previously collected evidence and memories are not rewritten.

`AdaptiveActionPolicy: true` automatically enables bounded episode
recording because the learner must train from the same episode objects
used for research exports.

```csharp
var run = new Engine(world,domain,host,new EngineOptions(
    AdaptiveActionPolicy: true,
    MaxExperiencePreferencesPerAgent: 48,
    MaxActionEpisodesPerAgent: 32)).Run();

Console.WriteLine($"Updates: {run.ExperienceUpdates}");
Console.WriteLine($"Preferences retained: {run.RetainedExperiencePreferences}");
```

## Exact learning rule and safeguards

For each agent, the key is `(highest private need ID, pressure bin,
action kind, action item)`. Pressure bins are `<30`, `30–59.999`
and `>=60`, with ordinal ties. A different dominant need or different
item has **no inherited preference**. The learner uses an exponentially
weighted moving average with learning rate `0.3`:

```text
signal = clamp(immediateDiagnosticProxy, -2, 2) if accepted
       = -1                                  if rejected
mean = mean + 0.3 * (signal - mean)
utilityBias = clamp(0.35 * mean, -0.4, +0.4)
```

No bias applies until at least **two independent action executions**
have been observed for the same actor/key. This prevents a single
outlier from immediately influencing choices, though it does not
solve correlated observations or unmeasured confounders.

Each agent retains at most 48 preference keys (configurable 1–512).
When full, the oldest inserted key is evicted deterministically;
sample counts are bounded and integer-saturated. The episode journal
remains separately capped. Actors never read each other's preferences.

Only the example survival domain's **non-social self-care and
preparation actions** receive the bonus (drink, eat, sleep, relieve,
warm, wash, treat, bandage, boil, cook and collecting resources).
**Accusations, spreading rumors, taking communal supplies and other
social actions receive zero preference bonus** from this simplistic
inventory-weighted diagnostic proxy.

When the adaptive policy is enabled, the existing exact-decision
certificate and fast LOD helpers return `null` so the full, auditable
decision path is always used. The opt-in integration uses
`IExperienceDecisionContext`, a separate interface so external
`IDecisionContext` implementations remain source compatible.

## Reproducible research

```bash
dotnet run --project tests/Enorys.Integration -c Release -- test
dotnet run --project tests/Enorys.Integration -c Release -- compare-learning
```

The integration suite tests zero cold-start, two-sample gating,
positive and negative feedback, distinct actor/state/action
preferences, bounded eviction, deterministic replays, resource
conservation, and a direct decision example in which a +0.4 bonus
switches the selected self-care action. The A/B CLI uses eight
identical initial seeds with and without learning, printing final
health, survivors, rejected actions, learned entry counts and whether
their action traces diverge.

### Crucial interpretation limits

- The signal derives from the **immediate Stage-13 observational proxy**.
  The method estimates an association between situation/action and
  state change; it cannot establish causality or long-term strategy quality.
  Next-activation drift is intentionally NOT used for credit assignment.
- Inventory units treat all items equally; reward hacking is possible.
  The conservative action allowlist and ±0.4 clamp limit but cannot
  remove this risk.
- The A/B seeds are **not** a held-out training/evaluation benchmark;
  the simulation learns and acts in the same run. No claim of improved
  intelligence or overall fitness is justified merely by different
  action traces.
- Enabling the policy incurs additional cognition and memory overhead
  and intentionally disables the certified fast paths.
- This is a light, tabular online preference system, not a trained
  neural model, reinforcement-learning convergence guarantee, or
  automatic improvement of the DIANA orchestrator.

Before a production adaptation, add independently validated reward
features, task-specific hard safety constraints, explicit exploration
budgets, frozen learned policy evaluation on separate episodes/worlds,
and statistical uncertainty across many seeds.

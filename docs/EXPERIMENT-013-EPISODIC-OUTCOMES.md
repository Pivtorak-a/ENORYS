# Experiment 013 — Episodic memory and observational outcome evaluation

**Stage:** first increment (diagnostics only). **Default:** disabled.
**Goal:** store actor-private episodes and describe observable short- and
next-activation state changes without affecting ENORYS's decision policy.

## What is recorded

`EpisodicOutcomeMemory` records one `ActionEpisode` for every executed
cognitive action, accepted or rejected. Each episode contains:

- Actor ID and world tick, chosen action kind and optional target
- Whether the World Adapter accepted the action and its result reason
- **Immediate delta:** actor health, average need pressure (lower is better),
  sum of inventory item counts, and a fixed proxy index
- **Next-activation delta:** change in the *same actor's private state*
  between the immediate post-action snapshot and that actor's next
  cognitive activation. Until the next activation, this field is null.

The journal reads only the actor's `AgentView` and its private
`IWorldAdapter.GetPrivateAgent` snapshot. It never asks World Authority
who committed an incident or reads another actor's evidence.

### Diagnostic proxy index

```text
healthDelta = healthAfter - healthBefore
needPressureDelta = meanNeedsAfter - meanNeedsBefore
inventoryUnitsDelta = totalItemsAfter - totalItemsBefore
proxy = clamp(0.05 * healthDelta - 0.03 * needPressureDelta
              + 0.10 * inventoryUnitsDelta, -10, +10)
```

Coefficients are **illustrative, not empirically validated**. Item units
are not priced or weighted by scarcity, and needs may have different
meaning in different domain packs. The index **is not a trained reward
function**, a proof of causal impact, or a quality score for an agent's
intelligence. If the world changes between activations, the delayed
delta also includes unrelated environment effects.

## Enable for a controlled run

```csharp
var run = new Engine(world, domain, host, new EngineOptions(
    TrackActionEpisodes: true,
    MaxActionEpisodesPerAgent: 32)).Run();

Console.WriteLine(run.TotalActionEpisodes);
foreach(var episode in run.ActionEpisodes)
    Console.WriteLine($"{episode.Tick} {episode.AgentId} " +
                      $"{episode.ActionKind} {episode.Immediate.ProxyScore}");
```

The default retention cap is **32 episodes per actor**; accepted range
is 1–2048. Within the engine, the previous episode for an actor is
completed when that same actor is activated next, even when other actors
have acted in the meantime. A terminal episode may never have a delayed
observation. When retention fills, the oldest entries are evicted;
`TotalActionEpisodes` counts all recorded actions.

`Inspect(agentId)` returns an independent, immutable actor-scoped
snapshot. `InspectAll()` and `EngineResults.ActionEpisodes` are
**privileged research exports**, not data exposed to ordinary NPC
decision code. Enabling this feature costs allocations and CPU;
high-scale use should benchmark with and without history capture.

## Reproducible validation

```bash
dotnet run --project tests/Enorys.Integration -c Release -- test
dotnet run --project tests/Enorys.Integration -c Release -- compare-episodes
```

The integration tests check immediate and next-activation arithmetic,
rejected actions, immutability of prior snapshots, eviction and
capacity validation, replay consistency, resource conservation,
and **exact action-trace parity** versus uninstrumented simulations
across three fixed seeds. The comparison CLI prints observations
and retention counts, not claims that learning has improved gameplay.

## Follow-up work

The next increment can study delayed credit assignment and a
configurable *opt-in* policy using past experience. That requires
controlled training/evaluation separation, multiple independent
seeds, computational-cost measurements, fail-closed policies and
comparison against the unchanged baseline before claiming improvement.

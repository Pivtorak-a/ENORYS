# ENORYS .NET 10 migration v0.5 — measured results

**Validation date:** 2026-10-12  
**Verification:** [GitHub Actions modular runtime build, integration tests, benchmark](https://github.com/Pivtorak-a/ENORYS/actions/runs/38203550697)  
**Status:** experimental modular C# end-to-end headless runtime; **not** a finished general-purpose cognitive SDK, and no Godot integration.

## What was actually ported from JS v0.4

- `Enorys.Abstractions`: public contracts for world, private NPC observation, decisions, modules, data packs.
- `Enorys.Core`: registry/validation for namespaced user data, prerequisite planner, plan reservations, episodic evidence-root deduplication, retractions, trust, social obligations, deterministic runtime, decision traces.
- `Enorys.Domains.Survival`: authoritative item ledger, nine physiological/social need categories, 14 item types, four recipes, filtered observations, taking, giving, requests, investigations, accusations, rumors, medical actions and needs progression. These mechanics are **domain-specific**, not part of generic Core.
- Cognitive LOD conservative fast path with 12 paired-seed comparisons to full deliberation; **fast path may provide no acceleration** at scale.
- `examples/Enorys.CustomBehavior`: a separate DLL built using **only Enorys.Abstractions**, dynamically loaded in integration tests and successfully executing a user-defined action on a user-defined need.
- `data-packs/custom-example.json`: extra need, object type, action registered without editing the Core source.
- Existing JS visual simulations and experiment logs remain in the repository as a **reference**, not dependencies of the C# kernel.

**Not implemented yet:** full Affective Cognitive Map (ADR-0002), durable snapshots/migrations, arbitrary item instances with contamination/durability, generalized planner operators, source-correlation inference, rule compiler, reliable third-party dependency resolution and sandboxing, Godot adapter, spatial indexing, actual NPC animation, zombies and real game loop. Note that the previous JS implementation was itself a limited research reference; there is no previously working full C# equivalent to copy.

## Test protocol

```bash
# Run from repository root with .NET 10 SDK:
dotnet build tests/Enorys.Integration/Enorys.Integration.csproj -c Release
dotnet run --project tests/Enorys.Integration/Enorys.Integration.csproj -c Release --no-build -- test
dotnet run --project tests/Enorys.Integration/Enorys.Integration.csproj -c Release --no-build -- bench
dotnet run --project tests/Enorys.Smoke/Enorys.Smoke.csproj -c Release
```

The integration suite exercises 24-NPC deterministic replay, plan completion, resource conservation, scarcity, invalid action rejection, rumor anti-echo, sender-authorized retractions, recipe rollback, dynamic compiled plugin and external data-pack effects, 12 paired LOD-policy seeds, a 40-seed scarcity fuzz set and 50/100/500-NPC finite balanced scenarios.

The existing .NET 10 mathematical smoke suite independently verifies bounded/exact Noisy-OR, explaining away, evidence deduplication, stable softmax and keyed RNG. Those primitives are **not yet wired into** the complete social policy; do not imply their presence proves full cognition realism.

## Benchmark environment and methodology

- GitHub-hosted **Ubuntu 24.04.5 LTS**, x64, **4** detected logical processors, **.NET 10.0.12**, Release build.
- **Five repeats** per configuration after one short warm-up. Seeds 1000–1004, identical per Full/LOD configuration; 14 days × 3 turns/day.
- Every run includes needs, social decisions, recipes, observed events, agent-private views and world validation.
- Entire-run elapsed **median**, not isolated decision-only latency. GC forced **between** measured repeats; no profiling instrumentation or tracing activated inside benchmark.
- Memory allocation measured with `GC.GetTotalAllocatedBytes`, CPU time using process `TotalProcessorTime`, memory from process `PeakWorkingSet64`. These measures include runtime/test framework overhead.
- Shared CI hardware can be noisy: the ranges overlap and CPU times can be affected by runtime GC threads and host scheduling.

### Measured headless simulation

| NPCs | Turns | Full (median) | LOD (median) | Full throughput | LOD throughput | Allocated/turn (full) | Process peak |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 24 | 1,008 | 16.53 ms | 15.76 ms | 60,965/s | 63,971/s | 8.65 KB | 69 MB |
| 50 | 2,100 | 54.18 ms | 53.51 ms | 38,761/s | 39,247/s | 9.02 KB | 76 MB |
| 100 | 4,200 | 112.87 ms | 71.70 ms | 37,212/s | 58,580/s | 9.49 KB | 78 MB |
| 500 | 21,000 | **302.53 ms** | **293.76 ms** | **69,416/s** | **71,488/s** | **12.45 KB** | **98.4 MB** |

Rounded values; see raw GitHub workflow logs for individual runs. Variability is large: 500-NPC Full individual repeats ranged **295.39–466.01 ms**, and LOD **286.49–304.86 ms**.

**Memory pressure:** 500-NPC runs allocated approximately **261.6 MB of short-lived objects** over 21,000 turns, resulting in roughly **16 gen-0, 4 gen-1 and 1 gen-2 GC events per repeat**. The *live peak process working set* was below 100 MB in this run, but the allocation rate is an optimization concern.

### Is this fast enough?

**Qualified answer: yes for a *headless reference* of 500 moderately detailed NPCs with infrequent cognitive updates; not yet proven for a full game at 60 FPS.**

At a hypothetical **one decision per NPC per real second**, a 500-NPC world requires 500 turns/s. The measured ~69k turns/s throughput suggests modest headless compute cost on this runner, *if action/event complexity is comparable*. But the same allocation rate produces about **6.2 MB of managed allocations per second**, so real-time workloads should implement event wakeups, batching and memory pooling.

If all 500 agents deliberated **60 times a second**, this would demand 30,000 decisions/s and nearly **374 MB/s temporary allocations** by linear projection, before any renderer/physics, which is not a sound design target. Use a mixed-rate scheduler instead. These are workload extrapolations, **not measured Godot framerates**.

**No defensible LOD speedup claim yet:** in the 500-agent sample only 0–2 decisions per 21,000-turn run qualified for the automatic fast path. The apparent differences in CPU or elapsed time must not be attributed to LOD; the test is dominated by noise/GC and full decisions.

## Architecture consequences

1. **Keep the modular headless C# runtime**. Performance is enough to continue architecture validation; no justification for rewriting in C++ yet.
2. **Reduce allocations before feature expansion:** replace ephemeral copied dictionaries/list LINQ scans with read-only versioned per-agent snapshots, per-zone indexes, pooled candidate lists and compact symbol IDs. Set an initial target of < **3 KB allocated per NPC action** while preserving actor isolation.
3. **Make LOD truly useful:** event-driven wake queue with bounded per-agent compute tokens; no fixed full deliberation every simulated turn. Instrument agent activations, cache-hit rates and invariance. Establish a target of ≥70% cheap choices under *defined* representative scenarios, without behavior divergence.
4. **Build the modular appraisal & ACM in .NET:** use actor-local evidence and typed provider interfaces, rather than hard-coded emotion enum switches.
5. **Implement snapshots and migrations** before Godot; record `engine version`, `module versions`, `data pack hashes`, `seed`, `event offsets`, `private state versions`.
6. **Godot only after benchmark reproducibility**, minimal physics/visibility scene and actual game-thread profiling at 30/60 FPS.

## Safety and external extensibility

- Data Packs are declarative; validation rejects invalid IDs, missing references and unknown actions.
- Behavior plugins are **trusted in-process .NET code**; `AssemblyLoadContext` is not a security sandbox.
- Only `IWorldAdapter.Execute` applies authoritative resources. Plugins receive private **copies** of agent inventories/needs and observation packets.
- Module ABI version negotiation, dependency manifest resolution and hostile plugin isolation are incomplete; do not call this a fully stable plugin marketplace/SDK.
- No open-source license has yet been selected by the maintainer; a public GitHub repo alone does not permit unlimited commercial reuse.

## Release classification

**v0.5 = verified experimental C# migration of current reference mechanics + preliminary performance measurement.** Remaining tasks are tracked as v0.6+ gates. Higher behavioral realism, full emotion maps, and full game content are **not** claimed.

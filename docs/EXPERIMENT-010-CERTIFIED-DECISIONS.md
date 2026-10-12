# Experiment 010 — Exact action certification in real C# ENORYS

**Date:** 2026-10-12  
**Status:** End-to-end experiment completed in source branch; opt-in only.  
**Related:** [ADR-0006](ADR-0006-CERTIFIED-DECISIONS.md), [Hybrid SurvivalWorld experiment 009](EXPERIMENT-009-HYBRID-SURVIVAL.md).  
**Verified CI:** [Exact Certified NPC Decisions, run 38214159091](https://github.com/Pivtorak-a/ENORYS/actions/runs/38214159091).

## Question and expected result

Can we accelerate ENORYS cognition **without missing required actions**, as the earlier skip-based scheduler did? A safe result must match the ordinary full policy on every simulated actor step and preserve private beliefs, resources, health, actor ordering and shared-world effects.

A one-turn certificate may save candidate-object generation if the exact highest-scoring current choice can be proven with a strict score gap. The proof must abort for planner/social/affect dependencies that are not known inactive; the original policy then executes.

**Expected for correctness:** zero independent audit mismatches, unchanged action-trace hashes, unchanged world event counts, health, action histograms and inventory conservation.

**Expected for performance:** fewer temporary allocations and faster compute in sufficiently quiet worlds. Extra certificate checks may be harmful in very busy worlds. Adaptive admission should avoid repeated unproductive proof attempts, but is itself heuristic for performance and may regress.

## Implemented C# components

- `ICertifiedDecisionModule.TryProve` plus `ActionChoiceCertificate` in `Enorys.Abstractions`.
- `SurvivalCertificate.cs`: numeric replica of every non-social candidate feasibility and exact score; strict `gap > 1e-10`; rejects unproven dependencies.
- `EngineOptions.CertifiedDecisions`: evaluate proof before the full `SurvivalBehavior.Choose`. World action execution and all authoritative rules **unchanged**.
- `EngineOptions.AuditCertificates`: shadow-run the independent full decision algorithm and throw on any action/candidate-count/numeric-score disagreement.
- `EngineOptions.AdaptiveCertificateAdmission`: three consecutive failed attempts trigger three turns of **ordinary** deliberation before another attempt; **not** a skipped action.
- `IAffectNeutrality`: plugin-provided, agent-specific certificate that emotional influence is provably zero; unknown affect plugins trigger conservative fallback.
- Cached actor-private `IndexedEvidenceGroup` collections avoid repeated grouping and root sorting after social memory stops changing; probability and temporal decay are still recomputed at each tick.

These do **not** implement long-horizon future decision stability or allow omitting world actions. Every actor turn is still processed; all certificates are one-turn, so temporal change and remote world inputs do not invalidate an assumption about future behavior.

## Falsification tests

The CI suite compares 48 scenario configurations (three populations: 24, 100 and 500 NPC; four seeds; ACM on/off; low/high social visibility), each in both static and adaptive audited proof modes. Then it repeats a separate **5,500-NPC, three-day whole-world action-history comparison**.

- **97 audited real-world cases** (96 small/medium + 1 large), plus their full reference runs.
- Exactly matching ordered action SHA-256, action counts by kind, event counts, health and living population; resources balanced.
- **218,721** successful independently shadow-audited certificates across the 96 parameterized runs, and **104,695** abstentions (large-world counters tested separately).
- Explicit large-world guard checks adaptive throttling and full-world trace equality, not merely aggregated health.
- The existing C# integration suite continues to pass.

Audit mode is intentionally slower and **is excluded** from the following timing comparisons.

## Measured 500 and 5,500 real NPC worlds

GitHub-hosted Ubuntu 24.04 x64, .NET 10 Release, full real SurvivalWorld/AgentMind/Planner/ACM. Three seeds (100–102) per row, seven days, three actor turns/day. All modes run the same initial world seeds, have **no skipped actions**, and keep the existing full World Authority. Values are medians of three elapsed run times in milliseconds and of transient managed allocation in MiB, with interleaved mode order. The benchmarks use `CaptureHistory:false` identically in each mode; the separate test suite validates the exact complete trace with capture enabled.

| NPC | Workload | Full baseline | Static certificate | Adaptive admission | Allocation full → static | Certified / nominal actions (static) |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 500 | Sparse social | **140.12 ms** | **116.23 ms** | 166.22 ms | 76.53 → 65.35 MiB | 6,540 / 10,500 |
| 5,500 | Sparse social | **692.78 ms** | **647.19 ms** | 652.50 ms | 789.28 → 646.68 MiB | 83,125 / 115,500 |
| 500 | Denser social | 119.70 ms | **111.68 ms** | 126.72 ms | 94.54 → 86.42 MiB | 4,644 / 10,500 |
| 5,500 | Denser social | 957.35 ms | 930.09 ms | **905.87 ms** | 877.33 → 770.53 MiB | 62,682 / 115,500 |

These are sample measurements on a **shared CI machine**, not a guaranteed speedup. In an earlier no-index run, static certificates were slower than baseline in some smaller/dense scenarios, and order/JIT/CPU variation materially affected absolute times. Three seeds are too few to demonstrate statistical significance of small timing differences. **The allocation reduction and exact equality checks are more robust findings than the small wall-time percentages.**

In the 5,500-agent sparse workload, static certification reduced temporary allocations by about **18.1%**, the mean population health and number of material actions remained identical, and ~72% of decisions could be certified.

In the denser social case the proof succeeds less often (~54% of decisions) because social interactions, obligations and emotional evidence prevent a conservative certificate. Adaptive admission avoids repeated certificate checks for these actors; in the observed 5,500-NPC run, the median timing was ~5.4% better than full baseline, but **this is not a validated universal advantage**.

**Performance tradeoff:** Adaptive admission is not consistently beneficial; it regressed on 500 NPC in this CI sample. Leave it opt-in rather than forcing it on all actors.

## Preservation of gameplay

| Property | Certified current-turn policy | Previous approximate PulseGraph sleep |
| --- | --- | --- |
| Actor decision evaluated each turn | Yes (proof or reference) | No |
| Action executed each nominal actor turn | **Yes** | No |
| World inventory/health/action-trace parity | **Exact in verified cases** | Can diverge |
| Social memory & ACM updates | Per original action/message timing | May shift with skipped actions |
| Benefit mechanism | Fewer candidate/epistemic object allocations | Fewer decisions entirely |
| Speed supported by full-world results | Small/variable win, memory reduction | No robust quality-preserving win |

## Open problems / next stage

1. Improve proof coverage without compromising fail-closed contracts: e.g., an exact bound for active plans, social obligations and bounded ACM contributions.
2. Introduce optional **long-horizon certificates** only for modules that prove continuous-time bounds on every relevant score, world-state invalidation and required action side effects. Never skip an objective world mutation just to claim speed.
3. Bound and index causal evidence and emotional episodes, avoid growing rumor histories indefinitely; preserve deterministic source provenance.
4. Measure GC pauses and isolated CPU profiling over many repetitions and on the intended SimWorld hardware.
5. Integrate World Adapter with Godot's active simulation/physics to verify real game frame budgets. Headless throughput alone is not a 60 FPS guarantee.

## Reproduce

```bash
dotnet run --project benchmarks/Enorys.HybridSurvival -c Release -- cert-test
dotnet run --project benchmarks/Enorys.HybridSurvival -c Release -- cert-bench
dotnet run --project tests/Enorys.Integration -c Release -- test
```

No native C/Zig/Go DLL is required, and the default engine behavior is unchanged unless `CertifiedDecisions` is explicitly enabled.

# Experiment 011 — ENORYS modular causal cognitive graph

**Date:** 2026-10-12  
**Branch:** `feature/v11-causal-graph-godot-bridge`  
**Implementation:** `src/Enorys.Cognition.Graph/` plus optional host/engine/survival adapters  
**Architecture:** [ADR-0007](ADR-0007-INCREMENTAL-CAUSAL-COGNITION.md)

## Delivered

- Separate **C#/.NET 10** causal cognition module, referenced only against `Enorys.Abstractions`.
- Event-indexed validated JSON rule packs: [base pack](../data-packs/causal-survival.json), [custom pack](../data-packs/causal-custom.json).
- Private observation → subjective belief → optional ACM appraisal → motivational goal → executed action DAG.
- Source-root anti-echo, provenance, sender-authorized retractions, inactive derived nodes and preserved historical actions.
- Lazy age-sensitive rule-specific utility influence, strict bounded sum into survival decision scores; unknown graph modules invalidate the fast exact-choice proof, preventing unsound certification.
- Actor-specific causal inspection and backward Decision Inspector (`Explain`).
- Root and action history budgets (96 each, configured by the base data pack), with edge cleanup and immutable snapshot output.
- Plugin registration supports additional cognitive rule data or other custom `ICausalGraphModule` implementations without modifying Core.

### Actual integration

An actor receives filtered observations through the established `SurvivalWorld.Observe` interface. The engine first updates the actor's `AgentMind`, and only then lets ACM and the causal module process the **same private observation**. The causal graph never sees `SurvivalWorld`, other NPC records or objective truth as inputs. Goal bias participates in the normal `SurvivalBehavior` utility comparison, which still submits actions to World Authority for validation.

A scripted but world-filtered multi-day sequence is tested: a witnessed claim about missing water, repetition of the same root by a different sender, and a retraction from the original source. The causal module stores the source chain, ignores echo as independent evidence and withdraws the unsupported goal after private correction. A separate gift triggers social-appraisal/reciprocity rules.

## Tests

CI: [Causal Graph Stage 11](https://github.com/Pivtorak-a/ENORYS/actions/workflows/causal-graph.yml).

- Source provenance and strict foreign-actor privacy checks.
- Backward Decision Inspector explanation includes observation, belief, affect, goal and action.
- Real choice influence proven on a constructed near-tie with standard SurvivalBehavior.
- Repeat-root anti-amplification, unauthorized and authorized corrections, lazy decay.
- Controlled bounded-memory stress: 160 new gift observations and no dangling links.
- JSON custom rules and duplicate rejection.
- **9 seeded actual-world configurations** (24/100/500 NPC, three seeds), comparing full original vs passive graph (`biasScale=0`), plus active graph and an independently repeated active run.
- Baseline vs passive graph: identical action-trace hashes, health and event counts in each tested world. Active causal graph: deterministic replay and conserved resources.
- Existing `.NET` integration tests still pass.

### First complete CI benchmark, before optimizing event-rule indexing

Ubuntu x64 GitHub-hosted runner, .NET 10 Release, **7 days × 3 turns/day**, median of three independently seeded runs. Graph and baseline both include the same existing ACM and decision modules. This is *headless*, no Godot physics or graphics.

| Agents | Full baseline | Passive causal graph | Active causal graph | Managed allocated baseline → active |
| ---: | ---: | ---: | ---: | ---: |
| 24 | 8.88 ms | 9.32 ms | 9.32 ms | 3.49 → 3.80 MiB |
| 100 | 36.74 ms | 48.18 ms | 48.64 ms | 15.50 → 18.46 MiB |
| 500 | **167.57 ms** | 280.33 ms | **250.62 ms** | **90.83 → 127.29 MiB** |

At 500 NPC, 10,500 nominal actor turns yielded about **35,536 retained causal nodes** in active mode (bounded per actor by the data pack); graph construction and action bias checks increased runtime and allocations. Values vary substantially across CI runs; avoid interpreting small mode-to-mode differences as statistically significant.

**Interpretation:** Stage 11 adds cognitive functionality and explainable provenance, **not** a general speedup. In a heavily social test the causal graph costs real resources. The legacy engine remains the default; the new module is enabled by explicitly installing it.

Further optimization already removes temporary `Where(...).ToArray()` event-rule filtering, groups rules by event ID at registration and replaces per-action LINQ ordering with deterministic iteration. Final measurements should be read from the run logs; exact runner noise prevents guaranteeing speedup.

## Limits

- This is a **functional first causal graph**, not a complete human cognitive model or a generic self-learning belief-propagation network. Bayesian belief evidence remains owned by `AgentMind`; graph motivation is a bounded additional score.
- Goal nodes bias existing registered decisions; they do not yet synthesize arbitrary multi-step planner operators or introduce new world actions.
- Emotion activation is taken from ACM's private episodes; it does not independently infer an unbounded hierarchy of emotions.
- Graph-root retention is bounded but durable historical provenance and save/load are **stage 13**, not included.
- Graph inspection API is privileged. It must never be exposed to an ordinary NPC as global mind access.
- No Godot integration or 60 FPS claims in this stage.

## Reproduce

```bash
dotnet run --project benchmarks/Enorys.CausalLab -c Release -- test
dotnet run --project benchmarks/Enorys.CausalLab -c Release -- bench
dotnet run --project tests/Enorys.Integration -c Release -- test
```

Next milestones: rich contradiction handling and psychological trajectories (stage 12), full graph snapshots (13), and Godot visual-world adapter (14).

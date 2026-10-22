# ENORYS

### A modular cognitive engine for autonomous game characters and AI agents

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

**ENORYS is an experimental, headless cognitive engine built in C# / .NET 10.** It explores how autonomous agents can perceive a world, form their own beliefs, remember events, develop goals, respond to social information, and make explainable decisions over time.

The project serves two connected purposes:

- **For games and simulations:** enable non-player characters (NPCs) with individual memories, motivations, social relationships, and context-dependent behaviour instead of relying exclusively on scripted reactions.
- **For AI research and agent systems:** provide a modular, inspectable environment for experimenting with memory, evidence, affect, planning, causal reasoning, and decision policies independently of any particular language model.

**Current status:** active research prototype. The .NET engine, runnable examples, automated tests, and reproducible experiments exist today. ENORYS is **not yet a production-ready SDK**, a complete model of human cognition, or a finished Godot integration.

## Why ENORYS exists

Creating an autonomous character is not the same as giving it a list of actions.

A character may need to remember who helped it, distrust a repeated rumour, adapt a plan when resources disappear, or behave differently because it witnessed an event another character did not. These behaviours depend on **internal state and experience**, not simply on the current scene.

Traditional finite-state machines and behaviour trees remain useful, efficient tools. But as interactions, memories, goals, and social dependencies accumulate, manually scripting every possible combination becomes difficult. Large language models can add flexible language and high-level reasoning, but making a separate model call for every character decision may introduce cost, latency, and difficulties with reproducibility or authoritative world-state control.

ENORYS investigates a complementary approach: **a reusable cognitive layer that can run without an LLM**, while leaving room for future optional language-model integration. The aim is not to replace existing game AI or LLMs. It is to make persistent agent state, decision logic, and their consequences more structured, testable, and understandable.

The central principle is simple:

> **An agent should act on what it has observed and come to believe—not on information it could not possibly know.**

This creates a foundation for agents whose actions reflect their individual histories, and for developers who can inspect *why* those actions happened.

## One architecture, two application areas

| Game characters and simulated worlds | Autonomous AI agents and research |
| --- | --- |
| NPCs with distinct memories and motivations | Experiments with agent-local memory and evidence |
| Social trust, rumours, obligations, and emotional responses | Inspectable, causally linked decisions |
| Goals and reactions that evolve with events | Modular decision policies and bounded planning |
| Headless simulation of many actors | Reproducible tests of cognitive mechanisms |
| Future adapters for game engines such as Godot | Future integration with external tools and optional LLMs |

ENORYS is **not a language model**. It does not currently generate dialogue or train a neural network. Instead, it studies the mechanisms around an agent's decision process: what the agent knows, what it values, what it intends to do, and which action the environment permits it to perform.

## How the engine works

```text
             External world / simulation host
             (the authoritative source of truth)
                            |
                    Filtered observation
                            |
                   +------------------+
                   |  ENORYS agent    |
                   |                  |
                   |  Private memory  |
                   |  Beliefs/evidence|
                   |  Social context  |
                   |  Affect (optional)|
                   |  Goals/planning  |
                   |  Causal graph    |
                   |    (optional)    |
                   |  Decision policy |
                   +------------------+
                            |
                      Action proposal
                            |
             World validates / executes
                            |
                 Result and new events
```

Three boundaries are fundamental:

1. **Private knowledge:** agents receive filtered observations; they do not directly read hidden global facts or other agents' private memories.
2. **World authority:** cognition proposes actions, while the world adapter validates and applies their effects.
3. **Explainability:** the system can preserve decision traces and, in the optional causal module, connect observations, beliefs, motivations, and actions.

For example, an agent can hear a claim about missing water without treating it as objective truth. Retellings from the same original source need not count as independent evidence. A valid later retraction can change the agent's current motivation, while an already executed action remains in its history. These are testable mechanisms, **not a claim that the engine reproduces human psychology**.

## What is implemented

The repository currently includes the following experimental components:

| Component | Current capability |
| --- | --- |
| **Headless .NET core** | Agent runtime and domain-independent abstractions, with a survival-world test domain |
| **Memory and evidence** | Agent-local observations, source provenance, social reports, and belief updates |
| **Goals and decisions** | Bounded planning and utility-based action selection |
| **Affective Cognitive Map (ACM)** | Optional, configurable emotion-related state and influence on decisions |
| **Causal cognition graph** | Optional observation → belief → affect/goal → decision links, inspection, and supported retractions |
| **Configuration and extension examples** | JSON rule/data packs and a sample external C# behaviour module |
| **Performance research** | Reproducible tests, CI benchmarks, scheduling experiments, and decision-certificate experiments |
| **Browser demonstrations** | Separate JavaScript reference sandboxes illustrating selected cognitive concepts |

The **C# engine is the main research implementation**. The browser demonstrations are independent reference examples and do not expose the full .NET engine.

The extension interfaces and examples are experimental. They should **not** be treated as a stable, versioned third-party SDK or a sandbox for untrusted plugins.

## Evidence, benchmarks, and engineering trade-offs

ENORYS is developed through reproducible experiments rather than performance or intelligence claims without measurements.

- A historical .NET benchmark processed **21,000 decisions for 500 NPCs in approximately 303 ms median** on a GitHub-hosted Linux runner. This is a headless workload, **not** an in-game frame-rate measurement. [Methodology](docs/NET-MIGRATION-BENCHMARK.md).
- The standalone **PulseGraph** scheduling experiment improved one sparse 5,000-agent threshold workload by **6.57×**, but it was slower in a dense-observation scenario. It is **not** a proven whole-engine speedup. [Experiment 008](docs/EXPERIMENT-008-PULSEGRAPH-BENCHMARK.md).
- Integration experiments with **5,500 NPCs** exposed cases where aggressive background scheduling changed outcomes or failed to improve total runtime. Those results inform the design instead of being hidden. [Experiment 009](docs/EXPERIMENT-009-HYBRID-SURVIVAL.md).
- The optional causal graph adds useful explanation and motivation mechanisms, but also adds CPU and memory overhead in tested social workloads. [Experiment 011](docs/EXPERIMENT-011-CAUSAL-GRAPH.md).
- **Experimental private belief comparison** can normalize competing accounts of one incident using unique evidence roots, deactivations and decay. This is an opt-in heuristic API, not an existing NPC policy or a calibrated probability model. [Experiment 012](docs/EXPERIMENT-012-CONFLICTING-PRIVATE-EVIDENCE.md).
- **Opt-in private belief timeline:** record bounded, evidence-driven revisions across simulated days for debugging and research; regression tests ensure normal NPC choices and outcomes are unchanged. [Experiment 012](docs/EXPERIMENT-012-CONFLICTING-PRIVATE-EVIDENCE.md).
- **Experimental belief-aware social policy:** optionally use alternative private hypotheses to prefer investigation over unsupported accusation, with two-independent-source gating and an A/B replay CLI. Default NPC choices are unchanged. [Experiment 012](docs/EXPERIMENT-012-CONFLICTING-PRIVATE-EVIDENCE.md).
- **Optional verified-source learning:** a trusted survival-world post-accusation verdict can adjust the acting NPC's private trust in prior witnesses, affecting later hearsay. This uses a synthetic verification oracle and is **off by default**. [Trust feedback limits](docs/EXPERIMENT-012-CONFLICTING-PRIVATE-EVIDENCE.md#verified-outcome-source-trust-fourth-increment).
- **Experiment 013 — episodic outcomes:** optional bounded actor-private history of actions, immediate outcomes, and changes observed at the next activation; preserves baseline decisions. The proxy index is diagnostic, **not causal reward or policy learning**. [Experiment 013](docs/EXPERIMENT-013-EPISODIC-OUTCOMES.md).

These measurements depend on the documented scenarios, hardware, and configurations. They do not establish general human-like intelligence, real-time game performance, or advantages over other agent frameworks.

## Try ENORYS

### Requirements

- **.NET 10 SDK** for the main C# engine.
- **Node.js 20+** for the separate JavaScript demonstrations and their tests.

From the repository root, run the C# integration tests:

```bash
dotnet run --project tests/Enorys.Integration/Enorys.Integration.csproj -c Release -- test
```

Run a seeded, reproducible NPC simulation that prints a JSON event story:

```bash
dotnet run --project tests/Enorys.Integration/Enorys.Integration.csproj -c Release -- simulate 42 24 14
```

Run the baseline benchmark or the causal cognition lab:

```bash
dotnet run --project tests/Enorys.Integration/Enorys.Integration.csproj -c Release -- bench
dotnet run --project benchmarks/Enorys.CausalLab -c Release -- test
```

For the independent browser demonstration:

```bash
npm ci
npm test
python3 -m http.server 8000
```

Then open `http://localhost:8000/` in a browser. The browser sandbox is a conceptual demonstration, not a frontend connected to the current .NET kernel.

## Repository guide

| Path | Purpose |
| --- | --- |
| [`src/Enorys.Abstractions`](src/Enorys.Abstractions) | Core contracts and extension interfaces |
| [`src/Enorys.Core`](src/Enorys.Core) | Runtime, memory/evidence, planning, decision, and social systems |
| [`src/Enorys.Domains.Survival`](src/Enorys.Domains.Survival) | Example world domain and action validation |
| [`src/Enorys.Affect.ACM`](src/Enorys.Affect.ACM) | Optional affective cognition |
| [`src/Enorys.Cognition.Graph`](src/Enorys.Cognition.Graph) | Optional causal cognition and explanation |
| [`examples/Enorys.CustomBehavior`](examples/Enorys.CustomBehavior) | External C# behaviour-module example |
| [`data-packs`](data-packs) | Example configuration and cognitive rule packs |
| [`tests`](tests), [`benchmarks`](benchmarks), [`experiments`](experiments) | Automated tests and repeatable research |
| [`docs`](docs) | Architectural decisions, methodology, results, and limitations |

## Development direction

The longer-term goal is a **reusable cognitive layer** that different simulation hosts and agent systems can adopt without hard-coding their game mechanics or infrastructure into ENORYS Core.

Priorities include:

1. **Persistence and longer-term cognition:** save/restore for agent state and causal history; richer belief revision and evolving relationships.
2. **Practical host integration:** a Godot adapter and a small interactive example that connects perception, decisions, and actions to a live game world.
3. **Extension maturity:** clearer, versioned module and data-pack contracts, compatibility tests, and developer documentation.
4. **Measurable scalability:** improve memory and scheduling while preserving agent behaviour where exact equivalence is required.
5. **Optional AI-model integration:** investigate using LLMs for dialogue or high-level guidance while keeping world validation and time-critical cognition under explicit control.

These are **development goals**, not shipped features. ENORYS does not currently provide a production Godot package, generic LLM interface, full durable cognitive snapshots, or a validated simulation of human minds.

## Why this work matters

Both interactive worlds and autonomous software agents face a similar engineering problem: **how to maintain a coherent decision-maker across time when information is incomplete, priorities compete, and actions have consequences**.

ENORYS approaches that problem with modular components, clear information boundaries, auditable causal traces, and reproducible experiments. Even when a future system uses an LLM, it still needs ways to manage observations, memory, permissions, world state, and action execution. ENORYS is an attempt to investigate and eventually provide reusable infrastructure for those responsibilities.

The value of the project is therefore not a claim to have created artificial consciousness or general intelligence. It is a practical research effort toward **more persistent, inspectable, and extensible autonomous behaviour**.

## Contributing and feedback

The project is in active experimental development. Bug reports, reproducible scenarios, design critiques, and benchmark comparisons are welcome through [GitHub Issues](https://github.com/Pivtorak-a/ENORYS/issues). For significant changes, please open an issue to discuss scope before submitting a pull request.

When reporting a behavioural or performance problem, include the scenario, seed, command, .NET version, expected result, and observed result where possible.

## License

ENORYS is licensed under the **[Apache License, Version 2.0](LICENSE)**.

Copyright 2026 Andrii Pivtorak. See the [NOTICE](NOTICE) file for attribution.

Apache 2.0 permits use, modification, distribution, and commercial integration, including in proprietary games and AI applications, subject to the license terms and preservation of required notices.
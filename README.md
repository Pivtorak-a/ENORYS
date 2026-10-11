# ENORYS · Cognitive Sandbox

**Experimental cognitive architecture for autonomous agents.** A small but runnable browser sandbox illustrating how partial observations, remembered events, social reports and explainable utility-based decisions can produce different histories from the same initial agents.

> **Status: interactive demo + experimental .NET 10 primitives.** This is **not** the full NOEMYN v0.1.1 specification, a complete C# SDK, or a validated model of psychology. [Architecture decision and tests](docs/ADR-0001-ARCHITECTURE.md).

## Try the demo

Open [`index.html`](./index.html) through a local HTTP server, or enable GitHub Pages in **Settings → Pages → Deploy from a branch → main → /(root)** to make it available in the browser. No external services, build tools, API keys or network requests are required at runtime.

```bash
# In a checkout of this repository:
python3 -m http.server 8000
# Open http://localhost:8000
```

Alternatively use Node.js 20+ to run the automated tests:

```bash
npm ci
npm test
```

### What you can do

- Create **6–60 agents** with seeded individual traits, resources and zones.
- Advance **one or 100 decisions** at a time; watch taking, giving, waiting and passing reports.
- Inspect every candidate action's **utility components**.
- Select an agent and inspect **eyewitness vs hearsay provenance** and changing relationships.
- Run a **counterfactual comparison** with the same seed and agents but 0% vs 100% witness chance.
- File issues with your seed, agent count, witness chance and steps so others can reproduce behavior.

## Headless C# core: mathematical validation

The repository includes **separate experimental .NET 10 primitives** (Noisy-OR, evidence-root de-duplication, stable softmax and keyed RNG), compiled and exercised in GitHub Actions. They are **not yet a complete autonomous-agent runtime**.

```bash
dotnet run --project tests/Enorys.Smoke/Enorys.Smoke.csproj --configuration Release
```

See [ADR-0001](docs/ADR-0001-ARCHITECTURE.md) for the selected architecture, alternatives, known limitations and release gates.

## What the prototype does NOT do

No natural language generation, LLM integration, lying, planner, persistence, learning, Godot adapter, validated emotions, or high-fidelity cognition. Rules and trait weights are **toy experimental defaults**, not psychologically calibrated. An audit log is available to the UI but never passed into an agent's decision function.

## Architecture

- `src/engine.js`: independent browser simulation of agents, observation, memories, rumors and world authority.
- `src/Enorys.Core/`: experimental .NET 10 mathematical primitives, separate from the JavaScript demo.
- `web/app.js`, `web/style.css`, `index.html`: dependency-free interactive UI.
- `tests/engine.test.js`: JavaScript determinism, provenance, utility, resource conservation and isolation.
- `tests/Enorys.Smoke/`: runnable .NET 10 numerical and deterministic invariant suite.
- `docs/ARCHITECTURE.md`: browser demo boundaries and known limitations.
- `docs/ADR-0001-ARCHITECTURE.md`: headless core architectural decision, alternatives and verification gates.

## Development and contribution

- `main`: tested public version.
- `develop`: integration branch.
- [Open a bug report or feature request](https://github.com/Pivtorak-a/ENORYS/issues). Please include reproduction inputs.
- If you'd like to contribute code, open an issue first to discuss scope.

## License

**No license chosen yet.** Code is public for inspection; public visibility by itself does not confer broad rights to copy, modify or distribute it. Maintainer will choose a code license before calling the project open source.

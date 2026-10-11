# ENORYS · Cognitive Sandbox

**Experimental cognitive architecture for autonomous agents.** A small but runnable browser sandbox illustrating how partial observations, remembered events, social reports and explainable utility-based decisions can produce different histories from the same initial agents.

> **Status: interactive demo + early .NET 10 core primitives.** This is **not** the full NOEMYN v0.1.1 specification, a complete C# SDK, or a validated model of psychology. [Architecture decision and validation gates](docs/ADR-0001-ARCHITECTURE.md).

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

The repository also includes **separate experimental .NET 10 primitives** (Noisy-OR, evidence-root de-duplication, stable softmax, keyed RNG), compiled and exercised by CI. They are not integrated into a full autonomous agent runtime yet.

```bash
dotnet run --project tests/Enorys.Smoke/Enorys.Smoke.csproj --configuration Release
```

Review [ADR-0001](docs/ADR-0001-ARCHITECTURE.md) for boundaries, architectural tradeoffs, exact verification scope and planned milestone tests.

## What the prototype does NOT do

No natural language generation, LLM integration, lying, planner, persistence, learning, Godot adapter, validated emotions, or high-fidelity cognition. Rules and trait weights are **toy experimental defaults**, not psychologically calibrated. An audit log is available to the UI but never passed into an agent's decision function.

## Architecture

- `src/engine.js`: browser-only simulation with seeded RNG, explicit local perceptions, memory, relationships, decisions and world authority.
- `src/Enorys.Core/`: experimental **C#/.NET 10** mathematical components, separate from the JS demo.
- `web/app.js`, `web/style.css`, `index.html`: dependency-free interactive UI.
- `tests/engine.test.js`: JS determinism, root provenance, utility transparency, conservation of resources, validation.
- `tests/Enorys.Smoke/`: standalone .NET 10 numeric and deterministic invariant runner.
- `docs/ARCHITECTURE.md`: boundaries, assumptions and known limitations.

## Development and contribution

- `main`: tested public version.
- `develop`: integration branch.
- [Open a bug report or feature request](https://github.com/Pivtorak-a/ENORYS/issues). Please include reproduction inputs.
- If you'd like to contribute code, open an issue first to discuss scope.

## License

**No license chosen yet.** Code is public for inspection; public visibility by itself does not confer broad rights to copy, modify or distribute it. Maintainer will choose a code license before calling the project open source.

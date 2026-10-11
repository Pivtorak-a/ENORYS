# ENORYS

**ENORYS** is an experimental cognitive architecture for autonomous agents in simulations and games.

The project explores agents that maintain memories, beliefs, relationships, motives and social norms, and select actions based on context and consequences rather than fixed behavior scripts.

## Planned architecture

- **World observations:** structured facts and events, with partial knowledge and uncertainty.
- **Memory and beliefs:** persistent facts, beliefs, and updates derived from observations.
- **Relationships and norms:** agent-specific relations, obligations, and social expectations.
- **Attention and salience:** prioritize what an agent notices and thinks about.
- **Decision engine:** low-cost routine decisions, with deliberate reasoning for important cases.
- **Cognitive level of detail:** adaptive computation budgets per agent.
- **Decision Inspector:** explain and replay decisions for debugging.

## Initial proof of concept

The first milestone targets a deterministic, testable simulation of **20–50 agents**, with most decisions handled by efficient non-LLM logic. LLM support, if added, should be optional and measured separately.

## Status

**Planning / scaffold.** There is no runnable cognitive engine in this repository yet. Architecture and interfaces are subject to revision as the proof of concept is developed.

## Development workflow

- `main`: reviewed, verified changes.
- `develop`: active implementation and testing.

## License

No open-source license has been selected yet. Public visibility alone does **not** grant permission to reuse this code.

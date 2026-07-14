# BattleCore MVP

Deterministic, event-driven, data-driven SLG BattleCore golden prototype for Python 3.11+.

This first version implements only 3v3 basic attacks, but keeps the production-oriented shape:

- `api.run_battle(input)` is the external entry point.
- Skill, effect and state configs are dataclasses shaped for future JSON/YAML/Excel import.
- All randomness goes through `DeterministicRNG`.
- Battle changes are emitted as JSON-serializable `BattleEvent` records with increasing `event_id`.
- Damage uses integer basis points and an extendable pipeline.
- Heroes are marked `exited=True` instead of being removed, preserving replay references.
- Main hero exit immediately finishes the battle.

Run the demo:

```bash
python -m battlecore.sample.sample_battle
```

Run tests:

```bash
pytest
```

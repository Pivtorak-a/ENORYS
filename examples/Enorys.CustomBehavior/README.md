# Example: custom .NET agent module

This compiles against **Enorys.Abstractions only**. It deliberately has no dependency on `Enorys.Core` or the survival world.

The registered `demo:focus-behavior` module reads the custom `demo:focus` need from `data-packs/custom-example.json` and proposes `demo:drink-tea`. The adapter validates the action, consumes one `survival:water` unit and reduces `demo:focus`.

How to test from repository root:

```bash
dotnet run --project tests/Enorys.Integration/Enorys.Integration.csproj -c Release -- test
```

This is a **trusted in-process** plugin illustration, not an untrusted code sandbox. Loader/module manifest compatibility, isolation, save-state migration and API ABI stability remain v0.6+ work.

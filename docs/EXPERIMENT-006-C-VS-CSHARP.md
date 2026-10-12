# Experiment 006 — Native C versus C# for ENORYS cognitive math

**Status:** Experimental optional native kernel; **not integrated into the production agent decision pipeline**.  
**Validation environment:** GitHub-hosted Ubuntu 24.04.5 LTS, x64, four logical processors, .NET 10.0.12, GCC `-O3 -march=x86-64`.  
**Reference CI:** [C versus C# Cognitive Math](https://github.com/Pivtorak-a/ENORYS/actions/runs/38205892591).  
**Code:** `native/enorys_math.c`, `native/enorys_math.h`, `benchmarks/Enorys.NativeMath/Program.cs`.

## Experimental design

We compared **the same log-space exact Noisy-OR model** from `Enorys.Core.Epistemics.NoisyOr`. This is a math-kernel benchmark, **not** a game engine or 500-NPC benchmark.

Four measured paths:

1. `C# canonical`: existing .NET implementation that returns allocated arrays/records.
2. `C# optimized`: hand-ported same algorithm, `stackalloc` scratch space, caller-owned result buffers, **zero significant per-solve managed allocations**.
3. `C scalar P/Invoke`: native C implementation, managed→native crossing once per solve.
4. `C batch P/Invoke`: managed→native crossing once per whole batch.

For all four, inputs are generated with the same deterministic seed, pre-allocated *outside* timed regions, written to caller-owned result buffers and summarized by a checksum. The native implementation is compiled with optimization (`-O3`) and fixed conservative x86-64 target (`-march=x86-64`), not unsupported `-march=native` optimizations.

**Correctness:** 2,000 independent random comparisons (1,000 with two causes, 1,000 with eight causes) against canonical .NET to absolute error at most `1e-11` on all posteriors and 3 diagnostics. Additional tests validate impossible events, input errors, certainty and leak extremes. Repeatability checked by output checksums.

**Measured iterations:** 40,000 solves × 2 causes and 2,500 solves × 8 causes. Five repetitions after warmup, forced GC outside measured calls. Reported are medians, not best-case times.

## Measurements

| Runtime | 2 causes × 40,000 | 8 causes × 2,500 | Allocated bytes / solve, 2 causes | Allocated bytes / solve, 8 causes |
| --- | ---: | ---: | ---: | ---: |
| Existing C# | **15.45 ms** | **51.90 ms** | 200 B | 4,280 B |
| Allocation-free C# | **6.66 ms** | **54.32 ms** | ~0 B | ~0 B |
| Scalar native C via P/Invoke | **4.87 ms** | **44.55 ms** | ~0 B | ~0 B |
| Batched native C via P/Invoke | **4.98 ms** | **44.64 ms** | ~0 B | ~0 B |

All numbers are preliminary on shared CI, and absolute time/throughput can vary.

### Speedups relative to existing C#

- 2 causes: **3.17×** speedup for native scalar C. Versus hand-optimized no-allocation C#: **1.37×**.
- 8 causes: **1.16×** speedup for native scalar C. Versus hand-optimized no-allocation C#: **1.22×**.

On small problems much of the initial improvement comes from **avoiding managed allocations**, not uniquely from using C. Conversely, allocation-free C# can be slower on eight causes (different JIT codegen / stack scratch use); benchmarking matters more than language intuition.

### Scalar versus batched native calls

The scalar ABI is already fast for this math kernel, and batching provides **no observed benefit** in these cases (sometimes slightly slower). Interop overhead is not the dominant cost here. Do not infer that batching is useless for other kernels with smaller workloads or different data layouts; profile representative calls.

## How to reproduce (Linux)

Install GCC and the .NET 10 SDK; from repository root:

```bash
gcc -O3 -march=x86-64 -fPIC -shared native/enorys_math.c \
  -o native/libenorys_math.so -lm -Wall -Wextra -Werror

dotnet build benchmarks/Enorys.NativeMath/Enorys.NativeMath.csproj -c Release

LD_LIBRARY_PATH="$PWD/native" dotnet run \
  --project benchmarks/Enorys.NativeMath/Enorys.NativeMath.csproj \
  -c Release --no-build
```

The native library name differs per platform (`libenorys_math.so` Linux, `enorys_math.dll` Windows, `libenorys_math.dylib` macOS). These other target platforms are **not yet built/tested** in CI. The existing C# engine works without this native library.

## Architecture recommendation

**Keep C# as the default module implementation.** Ship native C as an *optional, CPU-intensive math accelerator* only after measuring a bottleneck in actual 500/1,000-NPC cognitive scenarios.

Suggested integration in a separate later PR:

- Define `INoisyOrKernel` or general `ICausalInferenceKernel` in public C# abstractions.
- Provide `ManagedKernel` fallback and optional `NativeKernel` loaded explicitly if a compatible library exists.
- Keep `Enorys.Core` free from mandatory native dependencies. The C ABI accepts packed numeric inputs; never pass managed object graphs, pointers to GC-owned data beyond a pinned call, or domain-specific NPC entities into C.
- Add Windows/Linux/macOS CI and a CPU feature/ABI/version compatibility contract before treating the C DLL as a redistributable plugin.
- Profile inference as a share of **whole-world** CPU first. If inference is only 2% of tick time, even an infinitely fast implementation can save at most ~2% of overall run time (Amdahl's law).

Do not treat a microbenchmark's 3.17× kernel acceleration as a prediction of overall ENORYS gameplay speed. Most current v0.6 runtime time is elsewhere (private state assembly, choice enumeration, memory and social messages); Noisy-OR is currently **not wired into that runtime's decision path**.

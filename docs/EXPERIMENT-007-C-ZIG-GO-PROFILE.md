# Experiment 007 — Which ENORYS workloads should move to C, Zig or Go?

**Date:** 2026-10-12  
**Status:** implemented and tested on GitHub Actions; experimental alternative choice backends, **not a claim of overall language superiority**.  
**Code:** `native/choice_argmax.c`, `native/choice_argmax.zig`, `native/go_choice/main.go`, `tests/Enorys.Integration/NativeChoices.cs`, `src/Enorys.Domains.Survival/SurvivalBehavior.cs`.  
**Runner:** Ubuntu 24.04 x64, .NET 10, GCC `-O3`, Zig 0.14.1 `ReleaseFast`, Go 1.23 `buildmode=c-shared`. Four available logical CPUs.

## What was measured

The same deterministic **argmax of candidate utility scores** was implemented as:
1. Existing C# policy (candidate enumeration, in-process argmax);
2. Optimized C# policy with a direct `for` loop instead of `LINQ Skip`;
3. Managed C# `ReadOnlySpan<double>` selection API;
4. Native C scalar DLL through P/Invoke;
5. Native Zig scalar DLL through P/Invoke;
6. Go c-shared library exported through cgo and P/Invoke.

Pure kernel tests used fixed vectors with first-win tie handling, negative values and rejection of NaN.
Full-world tests built and ran the **real C# NPC simulator**, not a synthetic isolated workload: world state, 14 days, 3 nominal turns per NPC per day, private perceptions, social memories, planning, decisions, world validation and material balances. Cognitive LOD disabled for consistent timing. Each backend used **identical initial seeds and input profiles**.

**Correctness:** the action trace hash matched exactly across all five runtimes (original/default C#, managed Span, C, Zig, Go) for 24, 100 and 500 NPC with two seeds, in 7-day simulations; real-game resource conservation was also checked. Equality depends on current identical decision contracts.

## Phase profiling to locate real costs

Instrumented 500-NPC simulated world (14 days, 21,000 turns), actual samples from [workflow run #38206643394](https://github.com/Pivtorak-a/ENORYS/actions/runs/38206643394). For a warmed later sample (seed 204, **218.81 ms**):

| Stage | ms | Fraction of end-to-end elapsed |
| --- | ---: | ---: |
| Candidate generation, decision/planning policy (includes argmax) | 130.56 | 59.7% |
| World action validation and authoritative effects | 30.18 | 13.8% |
| Private agent view | 22.56 | 10.3% |
| Private perception packet | 10.31 | 4.7% |
| Other runtime + Advance + IsAlive | 25.19 | 11.5% |

Earlier samples include JIT/tiered-compilation and large scheduling spikes and are **not representative steady-state**. Each timer uses `Stopwatch.GetTimestamp` inside a decorated `IWorldAdapter` and thus includes some instrumentation overhead. Decision-phase time is **not the cost of argmax alone**.

### Realistic hot-path candidates

| Candidate | Port to native? | Engineering evidence |
| --- | --- | --- |
| Exact Noisy-OR/log-space numerical inference, large batched Bayesian and mathematical kernels | **Potentially C/Zig, optional** | Prior exact C kernel microbenchmark reached 3.17× over allocating C# at 2 causes and 1.16× at 8, with identical numerical outputs; **Noisy-OR is not yet in live NPC decisions** |
| Dense numeric emotional appraisal/similarity matrices (future) | **Only after profiling** | Current ACM is a sparse agent/target/root object graph; indexing/data layout matters more than language choice |
| Sorting/selecting a few candidate utility scores | **No** | Managed Span faster than individual native calls in microbenchmark; negligible whole-world gain |
| Planner prerequisites and reactive decision constraints | **Keep C# first** | Graph traversals, dictionaries, heterogeneous operators and actor-private data cause marshaling complexity; directly optimize algorithm and allocations |
| Episodic memory, rumor provenance, actor isolation, event scheduler, module loader, world authority | **Keep C#** | They operate on validated domain objects and require stable public APIs and safety checks |
| Batching large independent vectors (future) | **Maybe C/Zig** | Only useful when batch width amortizes marshaling/call overhead and preserves deterministic evidence provenance |
| Go native math DLL | **Not recommended for per-NPC hot paths** | c-shared includes a Go runtime and per-call boundary; measured substantially slower for short functions |

## 100,000 argmax invocations — tiny numerical kernel

**Medians from [workflow #38206783779](https://github.com/Pivtorak-a/ENORYS/actions/runs/38206783779):**

| Vector length | C# Span | C | Zig | Go |
| ---: | ---: | ---: | ---: | ---: |
| 8 | **0.637 ms** | 1.912 ms | 2.028 ms | 11.113 ms |
| 32 | **2.512 ms** | 3.617 ms | 3.741 ms | 14.829 ms |
| 64 | **5.027 ms** | 5.918 ms | 6.082 ms | 17.887 ms |

This includes *interop call overhead* for native languages, and is therefore representative of the chosen ENORYS .NET→native interface, **not a standalone binary-to-binary language benchmark**. The Go c-shared runtime is particularly costly across that boundary.

## Full 500-NPC reference simulation

**Medians of five full 21,000-turn runs, matched seeds 500–504, Release, experiment #38206783779:**

| Backend | Total ms | Difference vs managed direct C# loop | Managed temporary allocation/turn |
| --- | ---: | ---: | ---: |
| C# direct `for` | 185.44 | reference | 10.303 KB |
| C# managed Span | **181.90** | −1.9% elapsed | 10.303 KB |
| Native C | 183.42 | −1.1% elapsed | 10.303 KB |
| Native Zig | 182.05 | −1.8% elapsed | 10.303 KB |
| Native Go | 189.64 | **+2.3% elapsed** | 10.303 KB |

The 24 and 100-NPC worlds were also measured, including high variability for small runs; inspect CI logs rather than claiming stable ranks. Native kernels do **not** improve overall managed memory pressure because the bulk of allocations arise from candidate creation, source/goal checks, and actor/world data structures.

An earlier run before replacing `LINQ Skip(1)` with the direct `for` loop measured 205.62 ms for the default C# versus 194.09 ms for Zig (5.6% observed reduction), but this comparison was invalidated as an argument for a language rewrite: a one-line algorithm/data-path change in C# produced comparable results. Do **not** compare absolute times between separate CI runs as an optimization proof.

**No statistically significant intrinsic lead for Zig over C/managed C# can be claimed.** Effects between these three are on the order of shared-runner variability. More repeats on fixed hardware and detailed CPU sampling are needed.

## Architectural recommendation

**C# is the default implementation language for the ENORYS modular cognitive engine.** Add optional `INumericKernel` backends to mathematically dense arrays, using the C ABI implemented in C or Zig, not direct access to managed object graphs. Ensure:
- graceful managed fallback (no required native DLL);
- same outcome, fixed tie-breaking, replay reproducibility and numerical validation;
- versioned ABI + platform/CPU-feature CI, no GC pointers retained across calls;
- native backend must beat optimized managed implementation on **whole-world** timing with repeatable gains worth extra packaging/maintenance;
- no default Go c-shared math modules for tiny hot-path calls; Go can still be useful for independent service/process tasks, not as a lower-level calculation acceleration layer.

**Next experiment:** instrument actual Noisy-OR usage after integrating its evidence-conditional inference model into a real cognitive decision path; batch many independent problems only if profiler confirms it dominates whole-world CPU. Prioritize sparse ACM indexing, candidate pooling, source caching and scheduler structure before a wholesale rewrite.

## Reproduce on Linux x64

```bash
# Requires .NET 10, gcc, zig 0.14.1 and Go 1.23 (with cgo)
gcc -O3 -fPIC -shared native/choice_argmax.c -o native/libenorys_choice_c.so -Wall -Wextra -Werror -lm
zig build-lib native/choice_argmax.zig -dynamic -O ReleaseFast -fPIC -femit-bin=native/libenorys_choice_zig.so
CGO_ENABLED=1 go build -buildmode=c-shared -o native/libenorys_choice_go.so native/go_choice/main.go
dotnet build tests/Enorys.Integration/Enorys.Integration.csproj -c Release
dotnet run --project tests/Enorys.Integration -c Release --no-build -- profile
LD_LIBRARY_PATH="$PWD/native" dotnet run --project tests/Enorys.Integration -c Release --no-build -- compare-native
```

The baseline ENORYS engine does not need Zig, Go or C runtime binaries. Windows and macOS binary loading has not yet been tested.

using System.Diagnostics;
using System.Text.Json;
namespace Enorys.PulseGraph;
internal static class Program {
    private static string Root(){
        string dir=Directory.GetCurrentDirectory();
        for(int i=0;i<9;i++){
            if(File.Exists(Path.Combine(dir,"data-packs/pulse-needs.json")))return dir;
            dir=Directory.GetParent(dir)?.FullName??throw new DirectoryNotFoundException();
        }
        throw new DirectoryNotFoundException("Run from ENORYS repository");
    }
    private static void AssertSame(RunResult baseline,RunResult pulse,string label){
        if(baseline.TraceHash!=pulse.TraceHash||baseline.Actions!=pulse.Actions||
            !baseline.ActionCounts.SequenceEqual(pulse.ActionCounts)||
            !baseline.FirstActions.SequenceEqual(pulse.FirstActions))
            throw new InvalidOperationException("Non-equivalent event histories: "+label+
                $" scan={baseline.TraceHash:X} pulse={pulse.TraceHash:X}, actions {baseline.Actions}/{pulse.Actions}");
        Console.WriteLine("PULSE_PARITY "+JsonSerializer.Serialize(new{
            label,agents=baseline.Agents,ticks=baseline.Ticks,signals=baseline.Signals,
            events=baseline.Stimuli,actions=baseline.Actions,hash=baseline.TraceHash.ToString("X16")
        }));
    }
    private static int Test(string root){
        var rules=Packs.Load(Path.Combine(root,"data-packs/pulse-needs.json"));
        var withCustom=Packs.Load(Path.Combine(root,"data-packs/pulse-needs.json"),
            Path.Combine(root,"data-packs/pulse-custom.json"));
        if(rules.Length!=9||withCustom.Length!=11||
           !withCustom.Any(x=>x.Id=="custom:radiation"))throw new Exception("Data pack extension failed");
        Console.WriteLine("PULSE_PACKS "+JsonSerializer.Serialize(new{
            baseline=rules.Length,extension=withCustom.Length,addedWithoutCoreRecompile=true
        }));
        int cases=0;
        foreach(var defs in new[]{rules,withCustom})
        foreach(int agents in new[]{24,500,5000})
        foreach(double stimuliPerAgent in new[]{0.0,.3,5.0})
        foreach(int seed in new[]{17,42,81}){
            const int ticks=1440;
            var events=Stimuli.Generate(seed,agents,ticks,defs.Length,stimuliPerAgent);
            var baseline=FullTickScan.Run(agents,ticks,defs,events,seed);
            var pulse=SparsePulseScheduler.Run(agents,ticks,defs,events,seed);
            AssertSame(baseline,pulse,
                $"modules={defs.Length},agents={agents},density={stimuliPerAgent},seed={seed}");
            cases++;
        }
        Console.WriteLine($"PULSE_TESTS PASS {cases} independently reproducible history comparisons");
        return 0;
    }
    private static void Benchmark(int agents,int ticks,double density,SignalDefinition[] definitions,
        int seed,int repetitions){
        var events=Stimuli.Generate(seed,agents,ticks,definitions.Length,density);
        var control=FullTickScan.Run(agents,ticks,definitions,events,seed);
        var alternate=SparsePulseScheduler.Run(agents,ticks,definitions,events,seed);
        AssertSame(control,alternate,$"BENCH {agents} density={density}");
        // Independent JIT warmup; do not include in measurement.
        var warmEvents=Stimuli.Generate(seed,24,100,definitions.Length,.2);
        FullTickScan.Run(24,100,definitions,warmEvents,seed);
        SparsePulseScheduler.Run(24,100,definitions,warmEvents,seed);
        var times=new double[2,repetitions];
        var alloc=new double[2,repetitions];
        for(int r=0;r<repetitions;r++){
            for(int j=0;j<2;j++){
                int method=(r+j)%2; // interleave to reduce warm/cold-order bias
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                long before=GC.GetTotalAllocatedBytes(true);
                var watch=Stopwatch.StartNew();
                var result=method==0
                    ?FullTickScan.Run(agents,ticks,definitions,events,seed)
                    :SparsePulseScheduler.Run(agents,ticks,definitions,events,seed);
                watch.Stop();
                if(result.TraceHash!=control.TraceHash||result.Actions!=control.Actions)
                    throw new Exception("Replay error during timed run");
                times[method,r]=watch.Elapsed.TotalMilliseconds;
                alloc[method,r]=(GC.GetTotalAllocatedBytes(true)-before)/1024.0/1024.0;
            }
        }
        static double Median(double[] values){Array.Sort(values);return values[values.Length/2];}
        var fullTimes=Enumerable.Range(0,repetitions).Select(i=>times[0,i]).ToArray();
        var sparseTimes=Enumerable.Range(0,repetitions).Select(i=>times[1,i]).ToArray();
        double full=Median(fullTimes),sparse=Median(sparseTimes);
        Console.WriteLine("PULSE_BENCH "+JsonSerializer.Serialize(new{
            agents,ticks,registeredSignals=definitions.Length,stimuliPerAgent=density,
            stimulusEvents=events.Length,matchingDecisions=control.Actions,
            actionTraceHash=control.TraceHash.ToString("X16"),
            fullScanMedianMs=full,sparseMedianMs=sparse,
            speedup=full/sparse,
            fullScanRangeMs=new[]{fullTimes[0],fullTimes[^1]},
            sparseRangeMs=new[]{sparseTimes[0],sparseTimes[^1]},
            fullScanMeanAllocationMiB=Enumerable.Range(0,repetitions).Average(i=>alloc[0,i]),
            sparseMeanAllocationMiB=Enumerable.Range(0,repetitions).Average(i=>alloc[1,i]),
            scanSignalChecks=control.Checks,sparseSignalChecks=alternate.Checks,
            scanMaterializations=control.Materializations,
            sparseMaterializations=alternate.Materializations,
            skippedAgentSlots=alternate.SkippedSlots,
            sampleActions=control.FirstActions
        }));
    }
    private static int Main(string[] args){
        try{
            string root=Root();
            var rules=Packs.Load(Path.Combine(root,"data-packs/pulse-needs.json"));
            var extended=Packs.Load(Path.Combine(root,"data-packs/pulse-needs.json"),
                Path.Combine(root,"data-packs/pulse-custom.json"));
            Console.WriteLine("PULSE_ENV "+JsonSerializer.Serialize(new {
                runtime=Environment.Version.ToString(),
                platform=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                logicalCpuCount=Environment.ProcessorCount,
                architecture="dense per-tick scan vs event-driven threshold-crossing priority queue",
                note="Only the reactive signal scheduler is tested; not full ENORYS memory, planning, ACL, Godot or ACM"
            }));
            string mode=args.FirstOrDefault()??"test";
            if(mode=="test")return Test(root);
            if(mode=="bench"){
                const int ticks=1440,repeats=5;
                Benchmark(500,ticks,.3,rules,42,repeats);
                Benchmark(5000,ticks,.3,rules,42,repeats);
                Benchmark(20000,ticks,.3,rules,42,repeats);
                Benchmark(5000,ticks,12.0,rules,42,repeats);
                Benchmark(5000,ticks,.3,extended,42,repeats);
                return 0;
            }
            throw new ArgumentException("Usage: dotnet run --project experiments/Enorys.PulseGraph -- [test|bench]");
        }catch(Exception e){Console.Error.WriteLine("PULSE_FAILURE "+e);return 1;}
    }
}

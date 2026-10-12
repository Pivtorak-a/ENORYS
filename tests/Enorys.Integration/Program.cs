using System.Diagnostics;
using System.Text.Json;
using Enorys.Abstractions;
using Enorys.Core.Domain;
using Enorys.Core.Modules;
using Enorys.Core.Runtime;
using Enorys.Domains.Survival;
using Enorys.CustomBehavior;
using Enorys.Core.Social;

static class Program {
    static int Main(string[] args){
        string root=LocateRoot();
        var domain=DomainRegistry.Load(Path.Combine(root,"data-packs/survival.json"));
        var host=new ModuleHost();
        host.Install(new SurvivalBehavior());
        return args.FirstOrDefault() switch {
            "bench"=>Bench(root,domain,host,args),
            "test"=>Tests(root,domain,host),
            _=>Help()
        };
    }
    static string LocateRoot(){
        string dir=Directory.GetCurrentDirectory();
        for(int i=0;i<8;i++){
            if(File.Exists(Path.Combine(dir,"data-packs/survival.json")))return dir;
            dir=Directory.GetParent(dir)?.FullName??throw new DirectoryNotFoundException("Can't find repository root");
        }
        throw new DirectoryNotFoundException("Can't find data-packs/survival.json");
    }
    static EngineResults Run(DomainRegistry d,ModuleHost h,int seed=42,int agents=24,int days=14,
       double resource=.55,bool planning=true,bool lod=true,bool social=true,bool audit=false){
        var world=new SurvivalWorld(d,new SurvivalConfig(seed,agents,days,3,resource));
        return new Engine(world,d,h,new EngineOptions(planning,social,lod,audit)).Run();
    }
    static int Help(){Console.WriteLine("Usage: dotnet run -c Release --project tests/Enorys.Integration -- [test|bench]");return 2;}
    static void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);Console.WriteLine("PASS "+label);}
    static int Tests(string root,DomainRegistry d,ModuleHost host){
        try{
            var single=Run(d,host,seed:42,days:10);
            Check(single.World.Turns==24*10*3,"all scheduled turns");
            Check(single.World.ResourcesBalanced,"conservation across recipes and transfers");
            Check(single.World.AverageHealth>=0&&single.World.AverageHealth<=100,"bounded health");
            var repeat=Run(d,host,seed:42,days:10);
            Check(single.TraceHash==repeat.TraceHash,"deterministic action trace");

            var sparse=Run(d,host,seed:17,days:12,resource:.4);
            Check(sparse.World.ResourcesBalanced,"scarcity ledger");
            Check(sparse.World.Events>0&&sparse.World.CandidateChecks>0,"actions and explainable candidates");

            var noPlan=Run(d,host,seed:42,planning:false,days:10);
            Check(noPlan.World.ResourcesBalanced,"reactive run maintains conservation");
            Check(single.PlansCompleted>0,"multi-step plans completed");

            // Run full policy and LOD on identical seeds, compare *action histories*.
            for(int seed=1;seed<=12;seed++){
                var with=Run(d,host,seed,days:7,lod:true,audit:true);
                var full=Run(d,host,seed,days:7,lod:false);
                Check(with.Divergences==0,"LOD shadow parity, seed="+seed);
                Check(with.TraceHash==full.TraceHash,"LOD action parity, seed="+seed);
            }
            var custom=DomainRegistry.Load(Path.Combine(root,"data-packs/survival.json"),
                                           Path.Combine(root,"data-packs/custom-example.json"));
            Check(custom.Needs.ContainsKey("demo:focus")&&custom.Items.ContainsKey("demo:herbal-tea"),
                "external custom need and item loaded");
            Check(custom.Actions.ContainsKey("demo:drink-tea"),"external action registered");
            var extra=new SurvivalWorld(custom,new SurvivalConfig(31,24,2));
            var before=extra.GetPrivateAgent(0);
            var action=extra.Execute(new ActionProposal(before.Id,"demo:drink-tea"));
            var after=extra.GetPrivateAgent(0);
            Check(action.Accepted&&after.Inventory["survival:water"]==before.Inventory["survival:water"]-1
                &&after.Needs["demo:focus"]<=before.Needs["demo:focus"],"custom effect executed without rebuilding Core");
            Check(extra.ResourcesBalanced,"custom action respects resource ledger");
            var mind=new AgentMind();
            var packet=new ObservationPacket(1,1,.4,
                new Dictionary<string,int>(),new Dictionary<string,bool>(),[],
                [new Message("evidence","A002",Incident:"E007",Root:"R07",Suspect:"A009",LogLikelihood:Math.Log(4),Source:"eyewitness",Tick:1),
                 new Message("evidence","A008",Incident:"E007",Root:"R07",Suspect:"A009",LogLikelihood:Math.Log(4),Source:"hearsay",Tick:1)]);
            mind.Ingest(packet);
            Check(mind.Evidence.Count==1,"one eyewitness root forwarded twice remains one evidence");
            mind.Ingest(packet with {Tick=2,Messages=[new Message("retract","A002",Root:"R07",Tick:2)]});
            Check(!mind.Evidence["R07"].Active,"source-authorized retraction deactivates private belief");
            var beforeInvalid=extra.GetPrivateAgent(0);
            var invalidProposal=extra.Execute(new ActionProposal(beforeInvalid.Id,"survival:boil"));
            var afterInvalid=extra.GetPrivateAgent(0);
            Check(!invalidProposal.Accepted&&beforeInvalid.Inventory.SequenceEqual(afterInvalid.Inventory),
                "rejected recipe must not mutate material state");

            var extraModuleHost=new ModuleHost();
            extraModuleHost.Install(new FocusBehavior());
            var pluginWorld=new SurvivalWorld(custom,new SurvivalConfig(22,24,3));
            var pluginRun=new Engine(pluginWorld,custom,extraModuleHost,
                new EngineOptions(Planning:false,Social:false,CognitiveLod:false)).Run();
            Check(pluginRun.World.Actions.GetValueOrDefault("demo:drink-tea")>0,
                "external C# plugin executes custom decisions end-to-end");
            Check(pluginRun.World.ResourcesBalanced,"plugin conserves resources");
            var rejected=extra.Execute(new ActionProposal(before.Id,"unknown:illegal-action"));
            Check(!rejected.Accepted&&extra.ResourcesBalanced,"unknown action rejected atomically");
            var all=Run(custom,host,days:3);
            Check(all.World.ResourcesBalanced,"extra pack works alongside default runtime");
            foreach(int size in new[]{50,100,500}){
                var r=Run(d,host,seed:12,agents:size,days:3,resource:.6);
                Check(r.World.ResourcesBalanced&&r.World.Turns==size*3*3,"large group "+size);
            }

            for(int seed=201;seed<=240;seed++){
                var variant=Run(d,host,seed,agents:24,days:6,resource:.3+(seed%8)*.2,lod:true);
                if(!variant.World.ResourcesBalanced||!double.IsFinite(variant.World.AverageHealth)||
                   variant.World.AverageHealth is <0 or >100)
                    throw new Exception("Stress invariant failed on seed "+seed);
            }
            Check(true,"40 varied scarcity and replay seeds");
            Console.WriteLine("RESULT integration tests PASSED");
            return 0;
        }catch(Exception e){Console.Error.WriteLine("RESULT integration tests FAILED: "+e);return 1;}
    }
    static int Bench(string root,DomainRegistry d,ModuleHost host,string[] args){
        int[] sizes=[24,50,100,500];int repetitions=5;
        Console.WriteLine("BENCH_ENV "+JsonSerializer.Serialize(new{
            dotnet=Environment.Version.ToString(),os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            processorCount=Environment.ProcessorCount,framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            repeats=repetitions,days=14,turnsPerDay=3,seed=42
        }));
        foreach(int n in sizes){
            foreach(bool lod in new[]{false,true}){
                // One untimed warmup per scenario.
                Run(d,host,42,n,days:3,lod:lod);
                var measurements=new List<object>();
                var times=new List<double>();var allocations=new List<double>();var cpu=new List<double>();
                for(int k=0;k<repetitions;k++){
                    GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                    long allocatedBefore=GC.GetTotalAllocatedBytes(true);
                    int gc0Before=GC.CollectionCount(0),gc1Before=GC.CollectionCount(1),gc2Before=GC.CollectionCount(2);
                    var proc=Process.GetCurrentProcess();
                    var processorBefore=proc.TotalProcessorTime;
                    var sw=Stopwatch.StartNew();
                    var outcome=Run(d,host,1000+k,n,days:14,lod:lod);
                    sw.Stop();
                    double alloc=GC.GetTotalAllocatedBytes(true)-allocatedBefore;
                    double cpuMs=(Process.GetCurrentProcess().TotalProcessorTime-processorBefore).TotalMilliseconds;
                    if(!outcome.World.ResourcesBalanced||outcome.World.Turns!=n*14*3||outcome.Divergences!=0)
                        throw new Exception("Benchmark invariant failed");
                    times.Add(sw.Elapsed.TotalMilliseconds);allocations.Add(alloc);cpu.Add(cpuMs);
                    measurements.Add(new {ms=sw.Elapsed.TotalMilliseconds,allocBytes=alloc,cpuMs,
                        gc0=GC.CollectionCount(0)-gc0Before,
                        gc1=GC.CollectionCount(1)-gc1Before,
                        gc2=GC.CollectionCount(2)-gc2Before,
                        decisions=outcome.World.Turns,automatic=outcome.Automatic,health=outcome.World.AverageHealth,
                        candidateChecks=outcome.CandidateChecks});
                }
                var ordered=times.Order().ToArray();double median=ordered[repetitions/2];
                double allocatedPerTurn=allocations.Average()/(n*14*3);
                var line=new{agents=n,lod,turns=n*14*3,medianMs=median,
                    minMs=ordered[0],maxMs=ordered[^1],meanCpuMs=cpu.Average(),
                    throughputTurnsPerSec=n*14*3/(median/1000),
                    allocatedBytesPerTurn=allocatedPerTurn,
                    cpuMicrosecondsPerTurn=1000*cpu.Average()/(n*14*3),
                    currentWorkingSetMb=Process.GetCurrentProcess().WorkingSet64/1048576.0,
                    meanAllocatedMb=allocations.Average()/1048576,
                    peakWorkingSetMb=Process.GetCurrentProcess().PeakWorkingSet64/1048576.0,
                    samples=measurements
                };
                Console.WriteLine("BENCH_RESULT "+JsonSerializer.Serialize(line));
            }
        }
        return 0;
    }
}

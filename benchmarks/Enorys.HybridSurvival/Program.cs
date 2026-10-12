using System.Diagnostics;
using System.Text.Json;
using Enorys.Abstractions;
using Enorys.Core.Domain;
using Enorys.Core.Modules;
using Enorys.Core.Runtime;
using Enorys.Domains.Survival;
using Enorys.Affect.ACM;

internal static class Program {
    private const int Active=500, Background=5000;
    private static string Root(){
        string dir=Directory.GetCurrentDirectory();
        for(int i=0;i<9;i++){
            if(File.Exists(Path.Combine(dir,"data-packs/survival.json")))return dir;
            dir=Directory.GetParent(dir)?.FullName??throw new DirectoryNotFoundException();
        }
        throw new DirectoryNotFoundException("Cannot locate repository");
    }
    private static void Check(bool success,string explanation){
        if(!success)throw new Exception("FAIL: "+explanation);
        Console.WriteLine("HYBRID_PASS "+explanation);
    }
    private sealed record Case(int Seed,int Agents,int Active,int Days,
        bool Sparse,int MaximumRounds,bool Affect,double Visibility,double Resources,
        bool Trace=true,bool Wheel=true,bool Conservative=false);
    private sealed record Record(EngineResults Outcome,SurvivalWorld World,
        double Milliseconds,double AllocationMiB,double CurrentWorkingSetMiB,
        int ActiveEmotionEpisodes,int BackgroundEmotionEpisodes,
        string Mode,int Seed){
        public object Summary()=>new{
            Mode,Seed,Agents=World.AgentCount,activeAgents=Mode=="full"?World.AgentCount:World.AgentCount==24?8:Active,
            backgroundAgents=Mode=="full"?0:World.AgentCount==24?16:Background,
            days=World.Config.Days,nominalTurns=Outcome.World.Turns,
            milliseconds=Milliseconds,allocationMiB=AllocationMiB,
            workingSetMiB=CurrentWorkingSetMiB,
            activeDecisions=Outcome.ActiveDecisions,
            backgroundDecisions=Outcome.BackgroundDecisions,
            skippedSlots=Outcome.SkippedDecisions,
            fastForwardCalls=Outcome.FastForwardCalls,
            interruptedWakes=Outcome.WakeInterruptions,
            wakeHeapHighWater=Outcome.CalendarHighWater,
            cognitiveWakeVetoes=Outcome.CognitiveWakeVetoes,
            messagesProcessed=Outcome.MessagesProcessed,
            memoryEvidenceMessages=Outcome.MemoryEvidenceProcessed,
            affectMessageVisits=Outcome.AffectMessagesProcessed,
            activeEmotionEpisodes=ActiveEmotionEpisodes,
            backgroundEmotionEpisodes=BackgroundEmotionEpisodes,
            worldActions=Outcome.World.Events,
            candidatesChecked=Outcome.CandidateChecks,
            averageHealth=Outcome.World.AverageHealth,
            alive=Outcome.World.Alive,
            rejected=Outcome.World.Rejected,
            plansCompleted=Outcome.PlansCompleted,
            balanced=Outcome.World.ResourcesBalanced,
            actions=Outcome.World.Actions,
            trace=Outcome.TraceHash,
            firstActions=World.Events.Take(5).ToArray()
        };
    }
    private static Record Run(string root,DomainRegistry domain,Case test){
        var host=new ModuleHost();
        host.Install(new SurvivalBehavior());
        host.Install(new SurvivalBackgroundDeadline(domain,
            Path.Combine(root,test.Conservative?"data-packs/survival-pulse-conservative.json":"data-packs/survival-pulse.json")));
        AffectiveMap? affect=null;
        if(test.Affect){
            affect=new AffectiveMap(Path.Combine(root,"data-packs/affect-basic.json"));
            host.Install(affect);
        }
        var world=new SurvivalWorld(domain,new SurvivalConfig(
            Seed:test.Seed,Agents:test.Agents,Days:test.Days,TurnsPerDay:3,
            Resources:test.Resources,Visibility:test.Visibility));
        // Identical trusted, world-filtered observations in every mode.
        // During the second round, the active zone is already moving but
        // background agents may be dormant; this tests early calendar invalidation.
        int receiver=test.Active+4;
        int receiver2=test.Active+9;
        long time=test.Agents+test.Active-3;
        if(time<world.TotalTurns && receiver2<test.Agents){
            world.ScheduleObservation(time,receiver,new Message("evidence","A002",
                Incident:"scenario:missing-water",Root:"scenario:eye/1",
                Suspect:"A003",LogLikelihood:Math.Log(8),
                Source:"eyewitness"));
            world.ScheduleObservation(time,receiver2,new Message("gift","A003",
                Item:"survival:water"));
            world.ScheduleObservation(time+test.Agents,receiver,
                new Message("retract","A002",Root:"scenario:eye/1"));
        }
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long allocated=GC.GetTotalAllocatedBytes(true);
        var sw=Stopwatch.StartNew();
        var outcome=new Engine(world,domain,host,
            new EngineOptions(CognitiveLod:false,Social:true,
                HybridActiveAgents:test.Sparse?test.Active:0,
                BackgroundMaximumRounds:test.MaximumRounds,
                BackgroundRoundsPerDay:3,
                CaptureHistory:test.Trace,NormalizeCognitiveClock:true,
                HybridUseTimingWheel:test.Wheel)).Run();
        sw.Stop();
        var allocationMiB=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576.0;
        int activeEmotion=0,backgroundEmotion=0;
        if(affect!=null){
            foreach(int i in new[]{0,Math.Min(test.Active-1,test.Agents-1)})
                activeEmotion+=affect.Inspect(world.GetPrivateAgent(i).Id,world.Tick/test.Agents).Count;
            foreach(int i in new[]{receiver,receiver2})
                if(i<test.Agents)
                    backgroundEmotion+=affect.Inspect(world.GetPrivateAgent(i).Id,world.Tick).Count;
        }
        return new Record(outcome,world,sw.Elapsed.TotalMilliseconds,allocationMiB,
            Process.GetCurrentProcess().WorkingSet64/1048576.0,
            activeEmotion,backgroundEmotion,
            test.Sparse?(test.Conservative?"pulse-wheel-conservative":test.Wheel?"pulse-wheel":"pulse-heap"):"full",test.Seed);
    }
    private static int Tests(string root,DomainRegistry domain){
        try{
            var config=new SurvivalConfig(42,24,5,3,.6);
            var serial=new SurvivalWorld(domain,config);
            var jumped=new SurvivalWorld(domain,config);
            for(int i=0;i<24*3*5;i++)serial.Advance();
            jumped.AdvanceSlots(24*3*5);
            for(int i=0;i<24;i++){
                var a=serial.GetPrivateAgent(i);
                var b=jumped.GetPrivateAgent(i);
                Check(a.Health==b.Health&&a.Needs.OrderBy(x=>x.Key).SequenceEqual(
                    b.Needs.OrderBy(x=>x.Key)),i==0?"clock fast-forward preserves daily physiology":
                    "actor "+i+" clock state parity");
            }
            Check(serial.GetMetrics().ResourcesBalanced&&jumped.GetMetrics().ResourcesBalanced,
                "clock fast-forward preserves material accounting");
            var timed=new SurvivalWorld(domain,config);
            timed.ScheduleObservation(20,9,new Message("evidence","A002",
                Incident:"test:clue",Root:"test:root",Suspect:"A003",
                LogLikelihood:Math.Log(8),Source:"eyewitness"));
            timed.AdvanceSlots(23);
            Check(timed.HasPendingObservation(9),"external stimulus survives fast-forward");
            Check(timed.Observe(9).Messages.Count==1&&!timed.HasPendingObservation(9),
                "private observation consumed exactly once");
            foreach(int seed in new[]{17,42,81}){
                var scenario=new Case(seed,24,8,4,false,1,true,.16,.75);
                var baseline=Run(root,domain,scenario);
                var allAwake=Run(root,domain,scenario with{Sparse=true});
                var allAwakeHeap=Run(root,domain,scenario with{Sparse=true,Wheel=false});
                Check(baseline.Outcome.TraceHash==allAwake.Outcome.TraceHash &&
                      baseline.Outcome.TraceHash==allAwakeHeap.Outcome.TraceHash,
                    "pulse maxRounds=1 exactly replays all actions, seed "+seed);
                Check(baseline.Outcome.World.AverageHealth==allAwake.Outcome.World.AverageHealth &&
                      baseline.Outcome.World.ResourcesBalanced&&allAwake.Outcome.World.ResourcesBalanced,
                    "pulse no-skip reproduces physiology and ledger, seed "+seed);
                Check(allAwake.Outcome.BackgroundDecisions>0 &&
                      allAwake.Outcome.SkippedDecisions==0,
                    "pulse no-skip is actually exercising background calendar");
            }
            var hybrid=Run(root,domain,new Case(42,24,8,6,true,9,true,.10,.8));
            Check(hybrid.Outcome.World.ResourcesBalanced&&hybrid.Outcome.World.Turns==24*6*3,
                "hybrid pulse preserves authoritative clock and item ledger");
            Check(hybrid.Outcome.SkippedDecisions>0&&hybrid.Outcome.FastForwardCalls>0,
                "real SurvivalWorld skips dormant background slots in batches");
            Check(hybrid.Outcome.WakeInterruptions>0,
                "timed private observation invalidates a future sleep deadline");
            Check(hybrid.Outcome.MemoryEvidenceProcessed>0 &&
                  hybrid.Outcome.AffectMessagesProcessed>0 &&
                  hybrid.BackgroundEmotionEpisodes>0,
                "background wake updates private beliefs and independent ACM");
            var heapVersion=Run(root,domain,new Case(42,24,8,6,true,9,true,.10,.8,Wheel:false));
            Check(hybrid.Outcome.TraceHash==heapVersion.Outcome.TraceHash,
                "heap and timing-wheel produce same ordered actions with private wakeups");
            var conservative=Run(root,domain,new Case(42,24,8,6,true,9,true,.10,.8,
                Conservative:true));
            Check(conservative.Outcome.World.ResourcesBalanced &&
                  conservative.Outcome.BackgroundDecisions>0,
                "conservative JSON background policy runs without modifying the Core");
            var repeat=Run(root,domain,new Case(42,24,8,6,true,9,true,.10,.8));
            Check(hybrid.Outcome.TraceHash==repeat.Outcome.TraceHash,
                "hybrid pulse replay deterministic with emotion and memory");
            var large=Run(root,domain,new Case(73,5500,500,1,true,9,false,.02,.7,false));
            Check(large.Outcome.World.ResourcesBalanced&&large.Outcome.World.Turns==5500*3,
                "real 500 active / 5000 background population passes conservation");
            Check(large.Outcome.ActiveDecisions==500*3&&large.Outcome.BackgroundDecisions>0,
                "active cohort runs every turn and background cohort exists");
            Console.WriteLine("HYBRID_TESTS PASS");
            return 0;
        }catch(Exception e){Console.Error.WriteLine("HYBRID_TESTS FAIL "+e);return 1;}
    }
    private static int Bench(string root,DomainRegistry domain){
        try{
            Console.WriteLine("HYBRID_ENV "+JsonSerializer.Serialize(new {
                dotnet=Environment.Version.ToString(),
                os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                cores=Environment.ProcessorCount,
                active=Active,background=Background,
                note="C# real SurvivalWorld, real private AgentMind/ACM/plan; hybrid deliberately changes background decisions; not a Godot FPS test"
            }));
            var warm=new Case(19,24,8,2,false,1,true,.1,.7,false);
            Run(root,domain,warm);Run(root,domain,warm with{Sparse=true});
            // Large-world warmup of actor snapshots and the emotion module.
            Run(root,domain,new Case(97,Active+Background,Active,1,false,1,true,.04,.7,false));
            Run(root,domain,new Case(98,Active+Background,Active,1,true,9,true,.04,.7,false));
            foreach((string label,double visibility,double resources,int days) scenario in new[]{
                ("sparse-social",.025,.70,7),
                ("denser-social",.18,.80,7)}){
                for(int seed=100;seed<103;seed++){
                    // Rotate test order to reduce JIT/GC/runner thermal bias.
                    int[][] orders=[[0,1,2,3],[2,3,0,1],[1,0,3,2]];
                    Record? full=null,conservative=null;
                    foreach(int variant in orders[seed-100]){
                        var result=Run(root,domain,new Case(seed,Active+Background,Active,
                            scenario.days,variant!=0,9,true,scenario.visibility,scenario.resources,
                            Trace:false,Wheel:variant!=2,Conservative:variant==3));
                        if(variant==0)full=result;
                        if(variant==3)conservative=result;
                        if(!result.Outcome.World.ResourcesBalanced ||
                           result.Outcome.World.Turns!=(Active+Background)*scenario.days*3)
                            throw new InvalidOperationException("World corrupt");
                        Console.WriteLine("HYBRID_BENCH "+JsonSerializer.Serialize(new{
                            scenario=scenario.label,
                            details=result.Summary()
                        }));
                    }
                    if(full==null||conservative==null)throw new Exception("Missing quality controls");
                    double healthDelta=conservative.Outcome.World.AverageHealth-
                        full.Outcome.World.AverageHealth;
                    if(Math.Abs(healthDelta)>1.5||conservative.Outcome.World.Alive!=full.Outcome.World.Alive)
                        throw new Exception("Conservative background LOD exceeds defined 7-day health/alive guardrail");
                    Console.WriteLine("HYBRID_QUALITY "+JsonSerializer.Serialize(new{
                        scenario=scenario.label,seed,healthDelta,livePopulation=full.Outcome.World.Alive,
                        conservesMaterials=conservative.Outcome.World.ResourcesBalanced,
                        conservativeSkipped=conservative.Outcome.SkippedDecisions,
                        maxAllowedHealthDeviation=1.5
                    }));
                }
            }
            Console.WriteLine("HYBRID_BENCHMARKS PASS");
            return 0;
        }catch(Exception e){Console.Error.WriteLine("HYBRID_BENCHMARKS FAIL "+e);return 1;}
    }
    public static int Main(string[] args){
        string root=Root();
        var domain=DomainRegistry.Load(Path.Combine(root,"data-packs/survival.json"));
        return args.FirstOrDefault() switch {
            "test"=>Tests(root,domain),
            "bench"=>Bench(root,domain),
            _=>2
        };
    }
}

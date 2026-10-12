using System.Diagnostics;
using System.Text.Json;
using Enorys.Abstractions;
using Enorys.Affect.ACM;
using Enorys.Cognition.Graph;
using Enorys.Core.Domain;
using Enorys.Core.Modules;
using Enorys.Core.Runtime;
using Enorys.Domains.Survival;

internal static class Program {
    private static string Root(){
        string path=Directory.GetCurrentDirectory();
        for(int i=0;i<10;i++){
            if(File.Exists(Path.Combine(path,"data-packs/causal-survival.json")))return path;
            path=Directory.GetParent(path)?.FullName??throw new DirectoryNotFoundException();
        }
        throw new DirectoryNotFoundException("Cannot locate ENORYS data packs");
    }
    private static void Assert(bool condition,string label){
        if(!condition)throw new InvalidOperationException("CAUSAL_FAIL "+label);
        Console.WriteLine("CAUSAL_PASS "+label);
    }
    private sealed record Result(EngineResults Run,CausalGraphModule? Graph,
        double Milliseconds,double AllocMiB,long NodeCount,long Recomputed,long Unchanged,
        long Invalidated,string Mode,int Agents,int Seed){
        public object Describe()=>new {Mode,Agents,Seed,Milliseconds,AllocMiB,
            NodeCount,Recomputed,Unchanged,Invalidated,
            Run.CausalObservations,Run.CausalDecisions,Run.Deliberate,
            Run.CandidateChecks,Run.World.Events,Run.World.AverageHealth,
            Run.World.Alive,Run.World.ResourcesBalanced,Run.TraceHash};
    }
    private static Result Simulate(string root,DomainRegistry domain,int agents,int seed,
        bool graphEnabled,bool observeOnly,bool affect,bool history,int days=7){
        var host=new ModuleHost();host.Install(new SurvivalBehavior());
        if(affect)host.Install(new AffectiveMap(Path.Combine(root,"data-packs/affect-basic.json")));
        CausalGraphModule? graph=null;
        if(graphEnabled){
            graph=new CausalGraphModule([
                Path.Combine(root,"data-packs/causal-survival.json"),
                Path.Combine(root,"data-packs/causal-custom.json")],
                biasScale:observeOnly?0:1);
            host.Install(graph);
        }
        var world=new SurvivalWorld(domain,new SurvivalConfig(seed,agents,days,3,.70,.14));
        // Observed information only; none of these stimuli reveal global truth.
        int first=agents+5,second=2*agents+5,third=3*agents+5;
        if(third<world.TotalTurns){
            world.ScheduleObservation(first,0,new Message("evidence","A002",
                Incident:"scenario:missing-water",Root:"scenario:eye/1",
                Suspect:"A003",LogLikelihood:Math.Log(7),Source:"eyewitness"));
            world.ScheduleObservation(second,0,new Message("evidence","A004",
                Incident:"scenario:missing-water",Root:"scenario:eye/1",
                Suspect:"A003",LogLikelihood:Math.Log(7),Source:"hearsay"));
            world.ScheduleObservation(third,0,new Message("retract","A002",Root:"scenario:eye/1"));
            world.ScheduleObservation(first+7,1,new Message("gift","A003",
                Item:"survival:water",Root:"scenario:gift/1"));
        }
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long before=GC.GetTotalAllocatedBytes(true);
        var watch=Stopwatch.StartNew();
        var result=new Engine(world,domain,host,new EngineOptions(
            CognitiveLod:false,Planning:true,Social:true,
            NormalizeCognitiveClock:true,CaptureHistory:history)).Run();
        watch.Stop();
        if(!result.World.ResourcesBalanced)throw new Exception("World resource ledger violated");
        return new Result(result,graph,watch.Elapsed.TotalMilliseconds,
            (GC.GetTotalAllocatedBytes(true)-before)/1048576.0,
            graph?.TotalNodes??0,graph?.TotalRecomputed??0,
            graph?.TotalUnchanged??0,graph?.TotalInvalidated??0,
            !graphEnabled?"baseline":observeOnly?"observer":"causal",agents,seed);
    }
    private static int Tests(string root,DomainRegistry domain){
        try{
            var path=Path.Combine(root,"data-packs/causal-survival.json");
            var custom=Path.Combine(root,"data-packs/causal-custom.json");
            var rules=CausalRules.Load(path,custom);
            Assert(rules.Rules.Length==6&&rules.Rules.Any(x=>x.Id=="customcognition:solidarity"),
                "external causal rules load without Core modifications");
            bool rejected=false;
            try{CausalRules.Load(path,path);}catch(InvalidDataException){rejected=true;}
            Assert(rejected,"duplicate external modules are rejected");

            var mockWorld=new SurvivalWorld(domain,new SurvivalConfig(12,24,3));
            var baselineActor=mockWorld.GetPrivateAgent(0);
            var evidence=new PrivateEvidence("R1","incident:water","A003",
                Math.Log(7),"eyewitness","A002",10);
            var actor=baselineActor with {
                Evidence=Array.AsReadOnly(new[]{evidence}),
                Trust=new Dictionary<string,double>{{"A002",12},{"A003",2}}
            };
            var first=new ObservationPacket(10,1,.5,new Dictionary<string,int>(),
                new Dictionary<string,bool>(),["A002","A003"],[
                    new Message("evidence","A002",Incident:"incident:water",
                        Root:"R1",Suspect:"A003",LogLikelihood:Math.Log(7),
                        Source:"eyewitness",Tick:10)
                ]);
            IReadOnlyList<EmotionActivation> emotions=[
                new EmotionActivation("affect:anger","A003","R1",.8,10),
                new EmotionActivation("affect:fear","A003","R1",.35,10)
            ];
            var graph=new CausalGraphModule([path,custom]);
            graph.Observe(actor,first,emotions,10);
            var snap=graph.Inspect(actor.Id);
            Assert(snap.Nodes.Any(n=>n.Stage=="observation")&&
                   snap.Nodes.Any(n=>n.Stage=="belief")&&
                   snap.Nodes.Any(n=>n.Stage=="affect")&&
                   snap.Nodes.Any(n=>n.Stage=="goal")&&
                   snap.Edges.Count>=3,"observation to belief to affect to goal DAG exists");
            Assert(graph.Inspect("A004").Nodes.Count==0,
                "one actor's private beliefs are never broadcast to other agents");
            Assert(graph.ActionBias(actor,"survival:investigate",null,10)>0,
                "causal goal affects registered action utility");

            graph.RecordDecision(actor,new Decision(new ActionProposal(actor.Id,
                "survival:investigate",Incident:"incident:water"),"deliberate",
                "uncertain claim",3,2.1),new ActionResult(true,"ok"),10);
            var acted=graph.Inspect(actor.Id);
            Assert(acted.Nodes.Any(n=>n.Stage=="decision")&&
                   acted.Edges.Any(e=>e.Relation=="motivated-action"),
                "executed decision records why an actor chose a real-world action");
            long previous=acted.Recomputed;int count=acted.Nodes.Count;
            graph.Observe(actor,first with{Tick=11},emotions,11);
            var repeated=graph.Inspect(actor.Id);
            Assert(repeated.Nodes.Count==count&&repeated.Recomputed==previous&&
                   repeated.Unchanged>0,"same root gossip cannot amplify a claim");
            graph.Observe(actor,first with {
                Tick=11,Messages=[new Message("retract","A008",Root:"R1",Tick:11)]
            },emotions,11);
            Assert(graph.ActionBias(actor,"survival:investigate",null,11)>0,
                "unauthorized retraction cannot modify private beliefs");
            var retracted=actor with {
                Evidence=Array.AsReadOnly(new[]{evidence with {Active=false}})
            };
            graph.Observe(retracted,first with {
                Tick=12,Messages=[new Message("retract","A002",Root:"R1",Tick:12)]
            },emotions,12);
            var after=graph.Inspect(actor.Id);
            Assert(after.Nodes.Any(n=>n.Stage=="goal"&&!n.Active)&&
                   after.Nodes.Any(n=>n.Stage=="decision"&&n.Active)&&
                   after.Invalidated==0 &&
                   graph.ActionBias(actor,"survival:investigate",null,12)==0,
                "authorized correction invalidates future goals, preserves historical decisions");
            Assert(graph.Inspect("A004").Nodes.Count==0,
                "private correction cannot reach unrelated minds");

            // Long-term state bounded in per-agent root and decision budgets.
            for(int i=0;i<160;i++){
                string source="gift:"+i;
                graph.Observe(actor,new ObservationPacket(20+i,2,.5,
                    first.Depot,first.SourcesKnown,first.Nearby,[
                        new Message("gift","A002",Root:source,Item:"survival:water",
                            Tick:20+i)
                    ]),[],20+i);
            }
            var bounded=graph.Inspect(actor.Id);
            Assert(bounded.Nodes.Count<=96*5+96 && bounded.Invalidated>0,
                "root budget prunes old causal episodes and all dangling edges");
            var known=bounded.Nodes.Select(n=>n.Id).ToHashSet(StringComparer.Ordinal);
            Assert(bounded.Edges.All(e=>known.Contains(e.From)&&known.Contains(e.To)),
                "every retained causal edge has both endpoints");

            foreach(int n in new[]{24,100,500})foreach(int seed in new[]{17,42,73}){
                var reference=Simulate(root,domain,n,seed,false,false,true,true);
                var observer=Simulate(root,domain,n,seed,true,true,true,true);
                Assert(observer.Run.TraceHash==reference.Run.TraceHash &&
                       observer.Run.World.AverageHealth==reference.Run.World.AverageHealth &&
                       observer.Run.World.Events==reference.Run.World.Events,
                    $"observe-only plug-in preserves exact original actions n={n} seed={seed}");
                var causal=Simulate(root,domain,n,seed,true,false,true,true);
                var repeat=Simulate(root,domain,n,seed,true,false,true,true);
                Assert(causal.Run.TraceHash==repeat.Run.TraceHash &&
                       causal.Run.World.AverageHealth==repeat.Run.World.AverageHealth &&
                       causal.Run.CausalObservations>0&&causal.Run.CausalDecisions>0,
                    $"active causal graph produces reproducible NPC narrative n={n} seed={seed}");
                if(n==24&&seed==17){
                    var graphSnap=causal.Graph!.Inspect("A001");
                    Assert(graphSnap.Nodes.Any(x=>x.Root=="scenario:eye/1"&&x.Stage=="goal"&&!x.Active),
                        "real world eyewitness, rumor and retraction update causal dependencies");
                }
            }
            Console.WriteLine("CAUSAL_TESTS PASSED");
            return 0;
        }catch(Exception error){Console.Error.WriteLine("CAUSAL_TESTS FAILED "+error);return 1;}
    }
    private static int Bench(string root,DomainRegistry domain){
        try{
            Console.WriteLine("CAUSAL_ENV "+JsonSerializer.Serialize(new{
                dotnet=Environment.Version.ToString(),
                os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                cpus=Environment.ProcessorCount,
                note="Real SurvivalWorld and AgentMind/ACM, not Godot or human psychology"
            }));
            Simulate(root,domain,24,9,false,false,true,false,2);
            Simulate(root,domain,24,9,true,false,true,false,2);
            foreach(int count in new[]{24,100,500}){
                foreach(int seed in new[]{301,302,303}){
                    foreach(int mode in new[]{0,1,2}){
                        var result=Simulate(root,domain,count,seed,mode!=0,mode==1,
                            affect:true,history:false);
                        Console.WriteLine("CAUSAL_BENCH "+JsonSerializer.Serialize(result.Describe()));
                    }
                }
            }
            Console.WriteLine("CAUSAL_BENCHMARKS PASSED");
            return 0;
        }catch(Exception error){Console.Error.WriteLine("CAUSAL_BENCHMARKS FAILED "+error);return 1;}
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

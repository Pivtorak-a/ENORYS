using System.Diagnostics;
using System.Text.Json;
using Enorys.Abstractions;
using Enorys.Core.Domain;
using Enorys.Core.Modules;
using Enorys.Core.Runtime;
using Enorys.Domains.Survival;
using Enorys.CustomBehavior;
using Enorys.Core.Social;
using Enorys.Affect.ACM;

static class Program {
    static int Main(string[] args){
        string root=LocateRoot();
        var domain=DomainRegistry.Load(Path.Combine(root,"data-packs/survival.json"));
        var host=new ModuleHost();
        host.Install(new SurvivalBehavior());
        return args.FirstOrDefault() switch {
            "bench"=>Bench(root,domain,host,args),
            "simulate"=>Simulate(domain,host,args),
            "bench-scheduler"=>BenchScheduler(domain,host),
            "bench-acm"=>BenchAcm(root,domain),
            "profile"=>Profile(domain),
            "compare-native"=>CompareNative(domain),
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

    static int Simulate(DomainRegistry d,ModuleHost host,string[] args){
        int seed=args.Length>1?int.Parse(args[1]):42;
        int agents=args.Length>2?int.Parse(args[2]):24;
        int days=args.Length>3?int.Parse(args[3]):14;
        var world=new SurvivalWorld(d,new SurvivalConfig(seed,agents,days,3,.75));
        var run=new Engine(world,d,host,new EngineOptions()).Run();
        var output=new {
            model="ENORYS C# modular v0.5",seed,agents,days,
            metrics=run.World,
            run.PlansCompleted,run.Automatic,run.Deliberate,run.TraceHash,
            recentEvents=world.Events.TakeLast(25).ToArray()
        };
        Console.WriteLine(JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
        return run.World.ResourcesBalanced?0:1;
    }
    static int Help(){Console.WriteLine("Usage: dotnet run --project tests/Enorys.Integration -c Release -- [test|bench|simulate seed agents days]");return 2;}

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

            // Stage 12: alternative hypotheses use actor-private reports only.
            var claims=new PrivateEvidence[]{
                new("root-a1","CASE-12","A",Math.Log(25),"eyewitness","W1",5),
                new("root-a1","CASE-12","A",Math.Log(25),"hearsay","W2",5),
                new("root-b1","CASE-12","B",Math.Log(4),"eyewitness","W3",5),
                new("root-conflict","CASE-12","A",Math.Log(100),"trace","W4",5),
                new("root-conflict","CASE-12","B",Math.Log(100),"trace","W4",5),
                new("other","CASE-OTHER","C",Math.Log(100),"eyewitness","W5",5)
            };
            var comparison=IncidentBeliefEstimator.Compare(claims,"CASE-12",5);
            Check(comparison.Alternatives.Count==2 &&
                comparison.Alternatives[0].Suspect=="A" &&
                comparison.Alternatives.All(h=>h.IndependentRoots==1) &&
                Math.Abs(comparison.UnknownShare+comparison.Alternatives.Sum(h=>h.Share)-1)<1e-12,
                "exclusive private hypotheses normalize and deduplicate source roots");
            var moreIndependent=IncidentBeliefEstimator.Compare(
                claims.Append(new PrivateEvidence("root-a2","CASE-12","A",
                    Math.Log(12),"eyewitness","W6",5)).ToArray(),"CASE-12",5);
            Check(moreIndependent.Alternatives.First(h=>h.Suspect=="A").Share>
                comparison.Alternatives.First(h=>h.Suspect=="A").Share,
                "independent positive evidence increases alternative support");
            var negative=IncidentBeliefEstimator.Compare(
                claims.Append(new PrivateEvidence("root-a-negative","CASE-12","A",
                    -Math.Log(10),"trace","W7",5)).ToArray(),"CASE-12",5);
            Check(negative.Alternatives.First(h=>h.Suspect=="A").Share<
                comparison.Alternatives.First(h=>h.Suspect=="A").Share,
                "contradictory negative evidence reduces suspect support");
            var revisedBeliefs=IncidentBeliefEstimator.Compare(
                claims.Select(e=>e.Root=="root-a1"?e with {Active=false}:e).ToArray(),
                "CASE-12",5);
            Check(revisedBeliefs.Alternatives.All(h=>h.Suspect!="A") &&
                revisedBeliefs.Alternatives.Single().Suspect=="B",
                "retraction removes unsupported alternative and preserves other sources");
            var aged=IncidentBeliefEstimator.Compare(claims,"CASE-12",20000);
            Check(aged.Alternatives.First(h=>h.Suspect=="A").Share<
                comparison.Alternatives.First(h=>h.Suspect=="A").Share,
                "stale evidence decays without inventing world observations");
            var absent=IncidentBeliefEstimator.Compare(claims,"CASE-MISSING",5);
            Check(absent.UnknownShare==1 && absent.Alternatives.Count==0,
                "unknown hypothesis retains full weight without private evidence");

            var beforeInvalid=extra.GetPrivateAgent(0);
            var invalidProposal=extra.Execute(new ActionProposal(beforeInvalid.Id,"survival:boil"));
            var afterInvalid=extra.GetPrivateAgent(0);
            Check(!invalidProposal.Accepted&&beforeInvalid.Inventory.SequenceEqual(afterInvalid.Inventory),
                "rejected recipe must not mutate material state");

            var extraModuleHost=new ModuleHost();
            extraModuleHost.LoadTrustedAssembly(typeof(FocusBehavior).Assembly.Location);
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


            var viewWorld=new SurvivalWorld(d,new SurvivalConfig(73,24,2));
            var oldView=viewWorld.GetPrivateAgent(0);
            int oldWater=oldView.Inventory.GetValueOrDefault("survival:water");
            var drinkResult=viewWorld.Execute(new ActionProposal(oldView.Id,"survival:drink"));
            var currentView=viewWorld.GetPrivateAgent(0);
            Check(drinkResult.Accepted && oldView.Inventory["survival:water"]==oldWater
                && currentView.Inventory["survival:water"]==oldWater-1,
                "immutable private snapshots retain old values after world mutation");
            var oldDepot=viewWorld.Observe(0);
            int oldSupply=oldDepot.Depot.GetValueOrDefault("survival:water");
            var takeResult=viewWorld.Execute(new ActionProposal(oldView.Id,"survival:take",
                Item:"survival:water"));
            var newDepot=viewWorld.Observe(0);
            Check(takeResult.Accepted&&oldDepot.Depot["survival:water"]==oldSupply
                &&newDepot.Depot["survival:water"]==oldSupply-1,
                "depot snapshot invalidated without altering previously issued observations");
            Check(!viewWorld.HasPendingObservation(4),"inbox initially empty");
            var helpResult=viewWorld.Execute(new ActionProposal("A001","survival:ask",
                Item:"survival:water",Target:"A005"));
            Check(helpResult.Accepted&&viewWorld.HasPendingObservation(4),
                "world message signals wake without consuming inbox");
            Check(viewWorld.Observe(4).Messages.Any(m=>m.Type=="request") &&
                !viewWorld.HasPendingObservation(4),"woken NPC receives the message exactly once");
            var wake=new WakeScheduler(3);
            wake.Schedule(1,1,2);
            Check(!wake.ShouldRun(1,4,false),"timed sleep skips routine cognition");
            Check(wake.ShouldRun(1,4,true)&&wake.Interrupted==1,
                "incoming private message interrupts a scheduled sleep");
            Check(wake.ShouldRun(1,10,false),"due wake resumes normal decision cycle");
            var reference=Run(d,host,seed:42,days:8);
            var scheduledWorld=new SurvivalWorld(d,new SurvivalConfig(42,24,8,3,.55));
            var scheduled=new Engine(scheduledWorld,d,host,
                new EngineOptions(EventScheduling:true)).Run();
            Check(scheduled.World.ResourcesBalanced&&scheduled.World.Turns==reference.World.Turns,
                "scheduler preserves world-clock progression and resources");
            Check(scheduled.SkippedDecisions>0&&scheduled.Deliberate<reference.Deliberate,
                "sleep policy reduces expensive decision activations");
            var scheduledWorld2=new SurvivalWorld(d,new SurvivalConfig(42,24,8,3,.55));
            var scheduledRepeat=new Engine(scheduledWorld2,d,host,
                new EngineOptions(EventScheduling:true)).Run();
            Check(scheduled.TraceHash==scheduledRepeat.TraceHash,"scheduled execution reproducible");
            var acm=new AffectiveMap(Path.Combine(root,"data-packs/affect-basic.json"));
            var mindView=extra.GetPrivateAgent(0);
            var signal=new ObservationPacket(10,1,.5,new Dictionary<string,int>(),
                new Dictionary<string,bool>(),[],
                [new Message("evidence","A002",Incident:"E1",Root:"R1",Suspect:"A003",
                    LogLikelihood:Math.Log(8),Source:"eyewitness",Tick:10),
                 new Message("gift","A003",Item:"survival:water",Tick:10)]);
            acm.Receive(mindView,signal);
            var emotional=acm.Inspect(mindView.Id,10);
            Check(emotional.Any(x=>x.EmotionId=="affect:anger"&&x.TargetId=="A003")&&
                  emotional.Any(x=>x.EmotionId=="affect:gratitude"&&x.TargetId=="A003"),
                  "anger and gratitude coexist toward same target");
            double initial=emotional.Where(x=>x.EmotionId=="affect:anger").Sum(x=>x.Intensity);
            acm.Receive(mindView,signal);
            double repeated=acm.Inspect(mindView.Id,10)
                .Where(x=>x.EmotionId=="affect:anger").Sum(x=>x.Intensity);
            Check(Math.Abs(initial-repeated)<1e-12,"rumor echo cannot duplicate emotional impulse");
            var corrected=signal with {Tick=11,Messages=[new Message("retract","A002",Root:"R1",Tick:11)]};
            acm.Receive(mindView,corrected);
            var afterCorrection=acm.Inspect(mindView.Id,11);
            Check(afterCorrection.Where(x=>x.EmotionId=="affect:anger").Sum(x=>x.Intensity)<initial*.5,
                "belief correction reduces anger without erasing gratitude");
            Check(afterCorrection.Any(x=>x.EmotionId=="affect:gratitude"&&x.Intensity>0),
                "independent positive affect survives correction");
            Check(double.IsFinite(acm.ActionBias(mindView,"survival:accuse","A003",11))&&
                  Math.Abs(acm.ActionBias(mindView,"survival:accuse","A003",11))<=.6,
                "mixed affect contributes bounded finite utility (fear may outweigh anger)");

            var customAffect=new AffectiveMap(Path.Combine(root,"data-packs/affect-custom.json"));
            customAffect.Receive(mindView,corrected);
            Check(customAffect.Inspect(mindView.Id,11).Any(x=>x.EmotionId=="customaffect:relief"),
                "custom emotion and event trigger loaded from JSON, no code edits");
            Check(acm.Inspect(mindView.Id,60).All(x=>x.Intensity<=
                afterCorrection.First(y=>y.EmotionId==x.EmotionId&&y.TargetId==x.TargetId&&y.EvidenceRoot==x.EvidenceRoot).Intensity),
                "lazy emotional decay monotone without new evidence");

            // Hidden objective truth is not part of an affect module's inputs.
            var noMessage=corrected with {Tick=12,Messages=[]};
            var beforeSilent=acm.Inspect(mindView.Id,12);
            acm.Receive(mindView,noMessage);
            Check(beforeSilent.SequenceEqual(acm.Inspect(mindView.Id,12)),
                "unobserved world changes cannot inject emotions");
            acm.ObserveOwnAction(mindView,new ActionProposal(mindView.Id,"survival:retract",
                Target:"A003",Root:"R1"),11);
            Check(acm.Inspect(mindView.Id,11).Any(x=>x.EmotionId=="affect:guilt"&&x.TargetId=="A003"),
                "own correction may create separate guilt episode");
            var affectHost=new ModuleHost();
            affectHost.Install(new SurvivalBehavior());
            affectHost.Install(new AffectiveMap(Path.Combine(root,"data-packs/affect-basic.json")));
            var affectWorld=new SurvivalWorld(d,new SurvivalConfig(41,24,6,3,.5));
            var affectRun=new Engine(affectWorld,d,affectHost,new EngineOptions()).Run();
            Check(affectRun.World.ResourcesBalanced&&affectRun.World.Events>0,
                "optional ACM integrates without compromising world material authority");
            var affectOff=new ModuleHost();
            affectOff.Install(new SurvivalBehavior());
            Check(Run(d,affectOff,41,24,6).World.ResourcesBalanced,
                "ACM module can be omitted without breaking core");
            Check(true,"event wake and ACM acceptance suite");
            Console.WriteLine("RESULT integration tests PASSED");
            return 0;
        }catch(Exception e){Console.Error.WriteLine("RESULT integration tests FAILED: "+e);return 1;}
    }


    static int Profile(DomainRegistry domain){
        // Prewarm the 500-agent simulation to remove first-use JIT/tiered compilation spikes.
        var warmHost=new ModuleHost();warmHost.Install(new SurvivalBehavior());
        var warmWorld=new SurvivalWorld(domain,new SurvivalConfig(199,500,3,3,.55));
        new Engine(warmWorld,domain,warmHost,
            new EngineOptions(CognitiveLod:false)).Run();
        Console.WriteLine("PROFILE_ENV "+JsonSerializer.Serialize(new{
            dotnet=Environment.Version.ToString(),os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            cpus=Environment.ProcessorCount,agents=500,days=14,turnsPerDay=3}));
        for(int seed=200;seed<205;seed++){
            var host=new ModuleHost();host.Install(new SurvivalBehavior());
            var baseWorld=new SurvivalWorld(domain,new SurvivalConfig(seed,500,14,3,.55));
            var world=new TimedWorld(baseWorld);
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            long allocation=GC.GetTotalAllocatedBytes(true);
            var sw=Stopwatch.StartNew();
            var outcome=new Engine(world,domain,host,new EngineOptions(CognitiveLod:false)).Run();
            sw.Stop();
            if(!outcome.World.ResourcesBalanced)throw new Exception("Invalid resource ledger");
            static double Ms(long ticks)=>ticks*1000.0/Stopwatch.Frequency;
            double choice=Ms(outcome.ElapsedDecisionTicks);
            double view=Ms(world.ViewTicks),observe=Ms(world.ObservationTicks);
            double execute=Ms(world.ExecutionTicks),advance=Ms(world.AdvanceTicks);
            double alive=Ms(world.AliveTicks);
            var entry=new {seed,wallMs=sw.Elapsed.TotalMilliseconds,
                choiceMs=choice,viewMs=view,observeMs=observe,executeMs=execute,
                advanceMs=advance,aliveMs=alive,
                remainingEngineMs=sw.Elapsed.TotalMilliseconds-choice-view-observe-execute-advance-alive,
                allocatedBytesPerTurn=(GC.GetTotalAllocatedBytes(true)-allocation)/(double)outcome.World.Turns,
                decisions=outcome.Deliberate,checks=outcome.CandidateChecks,
                resourceBalanced=outcome.World.ResourcesBalanced};
            Console.WriteLine("PROFILE_RESULT "+JsonSerializer.Serialize(entry));
        }
        return 0;
    }
    static int CompareNative(DomainRegistry domain){
        IChoiceKernel[] backends=[new ManagedSpanChoice(),new NativeChoice("C"),
            new NativeChoice("Zig"),new NativeChoice("Go")];
        ReadOnlySpan<double> sample=[-2.0,4.0,4.0,0.4];
        foreach(var backend in backends){
            if(backend.ArgMax(sample)!=1)throw new Exception("Tie handling mismatch "+backend.Name);
            if(backend.ArgMax([-2.0,-3.0])!=0)throw new Exception("Negative choice mismatch "+backend.Name);
            if(backend.ArgMax([1,double.NaN])!=-1)throw new Exception("Nonfinite invalid input accepted: "+backend.Name);
        }
        Console.WriteLine("NATIVE_CHOICE_CORRECTNESS synthetic vector/boundary tests passed");
        foreach(int candidateCount in new[]{8,32,64}){
            var data=Enumerable.Range(0,candidateCount)
                .Select(i=>(double)((i*17)%23)/11-1).ToArray();
            foreach(var backend in backends){
                // Warm JIT and foreign runtimes ahead of timed calls.
                for(int i=0;i<1000;i++)backend.ArgMax(data);
                double[] samples=new double[5];int checksum=0;
                for(int k=0;k<5;k++){
                    var timer=Stopwatch.StartNew();
                    int subtotal=0;
                    for(int i=0;i<100000;i++)subtotal+=backend.ArgMax(data);
                    timer.Stop();samples[k]=timer.Elapsed.TotalMilliseconds;
                    checksum+=subtotal;
                }
                Array.Sort(samples);
                Console.WriteLine("NATIVE_KERNEL_BENCH "+JsonSerializer.Serialize(new{
                    backend=backend.Name,candidateCount,iterations=100000,
                    medianMs=samples[2],minMs=samples[0],maxMs=samples[4],checksum
                }));
            }
        }

        string[] names=["C# direct loop","C# span","C","Zig","Go"];
        var kernels=new IChoiceKernel?[]{null,backends[0],backends[1],backends[2],backends[3]};
        foreach(int agents in new[]{24,100,500}){
            foreach(int seed in new[]{42,71}){
                string? reference=null;
                for(int k=0;k<names.Length;k++){
                    var host=new ModuleHost();
                    host.Install(new SurvivalBehavior(kernels[k]));
                    var world=new SurvivalWorld(domain,new SurvivalConfig(seed,agents,7,3,.55));
                    var run=new Engine(world,domain,host,new EngineOptions(CognitiveLod:false)).Run();
                    if(!run.World.ResourcesBalanced)throw new Exception("Ledger changed: "+names[k]);
                    reference??=run.TraceHash;
                    if(reference!=run.TraceHash)throw new Exception(
                        "Full 7-day decision history diverged: "+names[k]+" / "+agents+" / "+seed);
                }
            }
        }
        Console.WriteLine("NATIVE_CHOICE_CORRECTNESS 24/100/500 NPC, 2 seeds, all 5 backends replay-identical");
        var measuredKernel=new TimedChoice(new ManagedSpanChoice());
        var instrumentedHost=new ModuleHost();
        instrumentedHost.Install(new SurvivalBehavior(measuredKernel));
        var instrumentedWorld=new SurvivalWorld(domain,new SurvivalConfig(612,500,14,3,.55));
        var instrumentedWatch=Stopwatch.StartNew();
        var instrumentedResult=new Engine(instrumentedWorld,domain,instrumentedHost,
            new EngineOptions(CognitiveLod:false)).Run();
        instrumentedWatch.Stop();
        Console.WriteLine("NATIVE_CHOICE_SHARE "+JsonSerializer.Serialize(new{
            agents=500,nominalTurns=instrumentedResult.World.Turns,
            elapsedMs=instrumentedWatch.Elapsed.TotalMilliseconds,
            fullDecisionMs=instrumentedResult.ElapsedDecisionTicks*1000.0/Stopwatch.Frequency,
            argmaxWithTimerMs=measuredKernel.Ticks*1000.0/Stopwatch.Frequency,
            argmaxCalls=measuredKernel.Calls,resourceBalanced=instrumentedResult.World.ResourcesBalanced
        }));


        for(int agentsIndex=0;agentsIndex<3;agentsIndex++){
            int agents=new[]{24,100,500}[agentsIndex];
            for(int k=0;k<names.Length;k++){
                // Warmup the JIT and foreign library without including in timing.
                var host=new ModuleHost();
                host.Install(new SurvivalBehavior(kernels[k]));
                var warm=new SurvivalWorld(domain,new SurvivalConfig(99,agents,3,3,.55));
                new Engine(warm,domain,host,new EngineOptions(CognitiveLod:false)).Run();
                double[] elapsed=new double[5],alloc=new double[5];
                for(int seed=0;seed<5;seed++){
                    var w=new SurvivalWorld(domain,new SurvivalConfig(500+seed,agents,14,3,.55));
                    GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                    long begin=GC.GetTotalAllocatedBytes(true);
                    var sw=Stopwatch.StartNew();
                    var result=new Engine(w,domain,host,new EngineOptions(CognitiveLod:false)).Run();
                    sw.Stop();
                    if(!result.World.ResourcesBalanced)throw new Exception("Invalid ledger");
                    elapsed[seed]=sw.Elapsed.TotalMilliseconds;
                    alloc[seed]=(GC.GetTotalAllocatedBytes(true)-begin)/(double)result.World.Turns;
                }
                Array.Sort(elapsed);
                Console.WriteLine("NATIVE_WORLD_BENCH "+JsonSerializer.Serialize(new{
                    agents,backend=names[k],nominalTurns=agents*14*3,medianMs=elapsed[2],
                    minMs=elapsed[0],maxMs=elapsed[^1],
                    allocatedBytesPerTurn=alloc.Average()}));
            }
        }
        return 0;
    }
    static int BenchAcm(string root,DomainRegistry d){
        Console.WriteLine("ACM_ENV "+JsonSerializer.Serialize(new{
            dotnet=Environment.Version.ToString(),
            platform=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            cpus=Environment.ProcessorCount,runs=3,days=14,turnsPerDay=3}));
        foreach(int agents in new[]{24,100,500})foreach(bool affectEnabled in new[]{false,true}){
            var timings=new List<double>();var allocations=new List<double>();var episodes=new List<int>();
            for(int i=0;i<3;i++){
                var host=new ModuleHost();
                host.Install(new SurvivalBehavior());
                AffectiveMap? affect=null;
                if(affectEnabled){
                    affect=new AffectiveMap(Path.Combine(root,"data-packs/affect-basic.json"));
                    host.Install(affect);
                }
                var world=new SurvivalWorld(d,new SurvivalConfig(300+i,agents,14,3,.55));
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                long before=GC.GetTotalAllocatedBytes(true);
                var watch=Stopwatch.StartNew();
                var result=new Engine(world,d,host,new EngineOptions(CognitiveLod:false)).Run();
                watch.Stop();
                if(!result.World.ResourcesBalanced)throw new Exception("ACM resource balance failure");
                timings.Add(watch.Elapsed.TotalMilliseconds);
                allocations.Add((GC.GetTotalAllocatedBytes(true)-before)/(double)result.World.Turns);
                episodes.Add(affect==null?0:Enumerable.Range(1,agents)
                    .Sum(n=>affect.Inspect($"A{n:D3}",world.Tick).Count));
            }
            timings.Sort();
            Console.WriteLine("ACM_RESULT "+JsonSerializer.Serialize(new{
                agents,affectEnabled,medianMs=timings[1],
                allocatedBytesPerTurn=allocations.Average(),
                averageAffectiveEpisodes=episodes.Average()
            }));
        }
        return 0;
    }
    static int BenchScheduler(DomainRegistry d,ModuleHost h){
        Console.WriteLine("SCHEDULER_ENV "+JsonSerializer.Serialize(new{
            framework=Environment.Version.ToString(),platform=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            cpus=Environment.ProcessorCount,days=14,turnsPerDay=3,repeats=5
        }));
        foreach(int n in new[]{24,100,500})foreach(bool scheduled in new[]{false,true}){
            var timings=new List<double>();var allocations=new List<double>();var skips=new List<long>();
            Run(d,h,42,n,days:3);
            for(int seed=1000;seed<1005;seed++){
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                var before=GC.GetTotalAllocatedBytes(true);
                var sw=Stopwatch.StartNew();
                var world=new SurvivalWorld(d,new SurvivalConfig(seed,n,14,3,.55));
                var result=new Engine(world,d,h,new EngineOptions(EventScheduling:scheduled)).Run();
                sw.Stop();
                if(!result.World.ResourcesBalanced)throw new Exception("bad scheduler balance");
                timings.Add(sw.Elapsed.TotalMilliseconds);
                allocations.Add((GC.GetTotalAllocatedBytes(true)-before)/(double)(n*14*3));
                skips.Add(result.SkippedDecisions);
            }
            timings.Sort();
            Console.WriteLine("SCHEDULER_RESULT "+JsonSerializer.Serialize(new{
                agents=n,scheduled,turns=n*14*3,medianMs=timings[2],
                minMs=timings[0],maxMs=timings[^1],
                allocatedBytesPerTurn=allocations.Average(),meanSkipped=skips.Average()
            }));
        }
        return 0;
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

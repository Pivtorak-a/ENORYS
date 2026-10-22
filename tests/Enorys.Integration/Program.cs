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
            "compare-beliefs"=>CompareBeliefPolicies(domain,host),
            "compare-episodes"=>CompareEpisodes(domain,host),
            "compare-learning"=>CompareAdaptiveLearning(domain,host),
            _=>Help()
        };
    }
    // Minimal decision context for isolated experimental social-policy checks.
    private sealed class SocialPolicyTestContext : IDecisionContext {
        public ActionProposal? PlannedStep(AgentView a,ObservationPacket p)=>null;
        public bool CanSpare(AgentView a,string item)=>false;
        public bool PlanningEnabled=>false;
        public bool SocialEnabled=>true;
        public bool LodEnabled=>false;
        public bool AuditLod=>false;
        public bool HasAffect=>false;
        public bool HasCausalGraph=>false;
        public double CausalBias(AgentView a,string kind,string? target,long tick)=>0;
        public bool IsAffectNeutral(AgentView a,long tick)=>true;
        public bool NoPlanCanActivate(AgentView a)=>true;
        public int CognitiveTickStride=>1;
        public double EmotionBias(AgentView a,string kind,string? target,long tick)=>0;
    }

    // Deterministic forced-action policy used only to test trusted feedback
    // through World Authority -> Engine -> one actor's private memory.
    private sealed class VerifiedTrustProbeDecision(string incident) : IDecisionModule {
        public string ModuleId=>"test:verified-trust-probe";
        public double? HearsayStrength {get;private set;}
        public double? TrustInSource {get;private set;}
        public Decision Choose(AgentView a,ObservationPacket p,IDecisionContext context) {
            if(a.Id=="A001"&&p.Tick==48){
                HearsayStrength=a.Evidence.FirstOrDefault(e=>e.Root=="future-claim")?.LogLikelihood;
                TrustInSource=a.Trust.GetValueOrDefault("W1");
            }
            var proposal=a.Id=="A001"&&p.Tick==24
                ?new ActionProposal(a.Id,"survival:accuse",Target:"A002",Incident:incident)
                :new ActionProposal(a.Id,"survival:wait");
            return new Decision(proposal,"deliberate","trusted-feedback test fixture",1,1);
        }
    }

    // Test-only experience context: validates that a controlled utility
    // bias can actually alter the selected action without touching the world.
    private sealed class ExperienceProbeContext(bool enabled):IDecisionContext,IExperienceDecisionContext {
        public ActionProposal? PlannedStep(AgentView a,ObservationPacket packet)=>null;
        public bool CanSpare(AgentView a,string item)=>false;
        public bool PlanningEnabled=>false;
        public bool SocialEnabled=>false;
        public bool LodEnabled=>false;
        public bool AuditLod=>false;
        public bool HasAffect=>false;
        public bool HasCausalGraph=>false;
        public int CognitiveTickStride=>1;
        public bool IsAffectNeutral(AgentView a,long tick)=>true;
        public bool NoPlanCanActivate(AgentView a)=>true;
        public double EmotionBias(AgentView a,string kind,string? target,long tick)=>0;
        public double CausalBias(AgentView a,string kind,string? target,long tick)=>0;
        public bool HasExperiencePolicy=>enabled;
        public double ExperienceBias(AgentView a,ActionProposal proposal)=>
            enabled && proposal.Kind=="survival:drink"?.4:0;
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

    // Descriptive A/B fixture: repeated seeded runs; no claim of superior
    // overall gameplay quality based on these short scenarios.
    static int CompareBeliefPolicies(DomainRegistry domain,ModuleHost host){
        var rows=new List<object>();
        foreach(int seed in new[]{41,73,87,113,145}) {
            (EngineResults Result, int Accuracies) RunScenario(bool enabled){
                var world=new SurvivalWorld(domain,new SurvivalConfig(seed,24,7,3,.65));
                world.ScheduleObservation(1,0,new Message("evidence","W1",
                    Incident:"CASE-CHOICE",Root:"claim-a",Suspect:"A002",
                    LogLikelihood:Math.Log(25),Source:"eyewitness",Tick:1));
                world.ScheduleObservation(25,0,new Message("evidence","W2",
                    Incident:"CASE-CHOICE",Root:"claim-b",Suspect:"A003",
                    LogLikelihood:Math.Log(25),Source:"eyewitness",Tick:25));
                world.ScheduleObservation(73,0,new Message("retract","W1",
                    Root:"claim-a",Tick:73));
                var results=new Engine(world,domain,host,
                    new EngineOptions(CompetingBeliefPolicy:enabled)).Run();
                return (results,world.AccurateAccusations);
            }
            var reference=RunScenario(false);
            var experimental=RunScenario(true);
            if(!reference.Result.World.ResourcesBalanced ||
               !experimental.Result.World.ResourcesBalanced)
                throw new InvalidOperationException("Resource conservation failure");
            rows.Add(new {
                seed,
                baseline=new {
                    reference.Result.TraceHash,
                    accusations=reference.Result.World.Actions.GetValueOrDefault("survival:accuse"),
                    investigations=reference.Result.World.Actions.GetValueOrDefault("survival:investigate"),
                    accurateAccusations=reference.Accuracies
                },
                competingBeliefs=new {
                    experimental.Result.TraceHash,
                    accusations=experimental.Result.World.Actions.GetValueOrDefault("survival:accuse"),
                    investigations=experimental.Result.World.Actions.GetValueOrDefault("survival:investigate"),
                    accurateAccusations=experimental.Accuracies
                },
                changedActions=reference.Result.TraceHash!=experimental.Result.TraceHash
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(new {
            disclaimer="Descriptive five-seed fixture only; no general quality or performance claim.",
            agents=24,days=7,rows
        },new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    }

    static void TestAdaptiveExperiencePolicy(DomainRegistry domain,ModuleHost host){
        var baseActor=new SurvivalWorld(domain,new SurvivalConfig(104,24,2))
            .GetPrivateAgent(0) with {
                Needs=new Dictionary<string,double>{
                    ["survival:thirst"]=65,["survival:hunger"]=35
                },
                Inventory=new Dictionary<string,int>{
                    ["survival:water"]=1
                }
            };
        var learner=new AdaptiveExperiencePolicy(maxEntriesPerActor:2,
            learningRate:.5,maximumUtilityBias:.4);
        var drink=new ActionProposal(baseActor.Id,"survival:drink");
        var eat=new ActionProposal(baseActor.Id,"survival:eat");
        string thirsty=AdaptiveExperiencePolicy.Situation(baseActor);
        var good=new ActionEpisode(baseActor.Id,1,drink.Kind,null,true,
            "accepted",new EpisodeOutcome(0,-20,-1,2),null);
        Check(learner.Bias(baseActor.Id,thirsty,drink)==0,
            "adaptive learner has a zero cold start");
        learner.Learn(baseActor,drink,good);
        Check(learner.Bias(baseActor.Id,thirsty,drink)==0,
            "single unconfirmed experience does not change policy");
        learner.Learn(baseActor,drink,good);
        double positive=learner.Bias(baseActor.Id,thirsty,drink);
        Check(positive>0&&positive<=.4&&learner.Bias(baseActor.Id,thirsty,eat)==0 &&
            learner.Bias("other-agent",thirsty,drink)==0,
            "repeat private experience creates bounded action-specific preference");
        var differentSituation=baseActor with {
            Needs=new Dictionary<string,double>{
                ["survival:thirst"]=20,["survival:hunger"]=75
            }
        };
        Check(learner.Bias(baseActor.Id,AdaptiveExperiencePolicy.Situation(differentSituation),drink)==0,
            "learning does not transfer across unrelated dominant needs");
        var rejected=new ActionEpisode(baseActor.Id,2,drink.Kind,null,false,
            "not_available",new EpisodeOutcome(0,0,0,0),null);
        for(int n=0;n<10;n++)learner.Learn(baseActor,drink,rejected);
        Check(learner.Bias(baseActor.Id,thirsty,drink)<0,
            "failed actions produce bounded negative preference");
        for(int i=0;i<4;i++)
            learner.Learn(baseActor,new ActionProposal(baseActor.Id,"survival:wait"),
                new ActionEpisode(baseActor.Id,i,"survival:wait",null,true,
                    "accepted",new EpisodeOutcome(0,0,0,0),null));
        learner.Learn(differentSituation,drink,good);
        Check(learner.RetainedPreferences==2 &&
            learner.TotalUpdates==17 &&
            learner.Inspect(baseActor.Id).Count==2,
            "bounded deterministic eviction retains only configured actor preferences");
        bool invalid=false;
        try{_ = new AdaptiveExperiencePolicy(0);}catch(ArgumentOutOfRangeException){invalid=true;}
        Check(invalid,"invalid adaptive capacity rejected");

        var neutral=baseActor with {
            Needs=new Dictionary<string,double>(),
            Inventory=new Dictionary<string,int>{{"survival:water",1}}
        };
        var packet=new ObservationPacket(1,1,.5,
            new Dictionary<string,int>(),new Dictionary<string,bool>(),[],[]);
        var behavior=new SurvivalBehavior();
        var baseChoice=behavior.Choose(neutral,packet,new ExperienceProbeContext(false));
        var adaptedChoice=behavior.Choose(neutral,packet,new ExperienceProbeContext(true));
        Check(baseChoice.Proposal.Kind=="survival:sleep" &&
            adaptedChoice.Proposal.Kind=="survival:drink" &&
            behavior.TryFastDecision(neutral,packet,new ExperienceProbeContext(true))==null &&
            behavior.TryProve(neutral,packet,new ExperienceProbeContext(true))==null,
            "bounded experience bias changes a close decision and disables unaudited fast paths");

        EngineResults RunExperience(int seed,bool enabled){
            var world=new SurvivalWorld(domain,new SurvivalConfig(seed,24,8,3,.65));
            return new Engine(world,domain,host,new EngineOptions(
                AdaptiveActionPolicy:enabled,MaxActionEpisodesPerAgent:5,
                MaxExperiencePreferencesPerAgent:16)).Run();
        }
        foreach(int seed in new[]{45,83,127}){
            var reference=RunExperience(seed,false);
            var experimental=RunExperience(seed,true);
            var repeat=RunExperience(seed,true);
            Check(experimental.TraceHash==repeat.TraceHash &&
                experimental.World.ResourcesBalanced && reference.World.ResourcesBalanced &&
                experimental.World.Turns==reference.World.Turns,
                "adaptive policy seed replay is deterministic and conserves resources "+seed);
            Check(reference.ExperienceUpdates==0 && reference.TotalActionEpisodes==0 &&
                experimental.ExperienceUpdates==experimental.TotalActionEpisodes &&
                experimental.ExperienceUpdates>0 &&
                experimental.RetainedExperiencePreferences<=24*16 &&
                experimental.ActionEpisodes.Count<=24*5,
                "adaptive learning is private, bounded and consumes real episodes "+seed);
        }
    }

    // Stage 013: observational episodic history. Zero influence on choices.
    static void TestEpisodicOutcomes(DomainRegistry domain,ModuleHost host){
        var world=new SurvivalWorld(domain,new SurvivalConfig(91,24,2));
        var original=world.GetPrivateAgent(0) with {
            Health=80,
            Needs=new Dictionary<string,double> {["hunger"]=40,["thirst"]=50},
            Inventory=new Dictionary<string,int> {["water"]=2}
        };
        var changed=original with {
            Health=84,
            Needs=new Dictionary<string,double> {["hunger"]=30,["thirst"]=20},
            Inventory=new Dictionary<string,int> {["water"]=1}
        };
        var next=original with {
            Health=83,
            Needs=new Dictionary<string,double> {["hunger"]=35,["thirst"]=25},
            Inventory=new Dictionary<string,int> {["water"]=2}
        };
        var memory=new EpisodicOutcomeMemory(maxEpisodesPerActor:2);
        var action=new ActionProposal(original.Id,"survival:drink");
        memory.Record(original,action,new ActionResult(true,"accepted"),changed,0);
        var first=memory.Inspect(original.Id).Single();
        Check(first.Immediate.HealthDelta==4 &&
            first.Immediate.NeedPressureDelta==-20 &&
            first.Immediate.InventoryUnitsDelta==-1 &&
            Math.Abs(first.Immediate.ProxyScore-.7)<1e-12 &&
            first.NextActivation==null,
            "episodic outcome captures immediate observable needs, health and inventory");
        memory.BeginActivation(next);
        var closed=memory.Inspect(original.Id).Single();
        Check(closed.NextActivation!=null &&
            closed.NextActivation.HealthDelta==-1 &&
            closed.NextActivation.NeedPressureDelta==5 &&
            closed.NextActivation.InventoryUnitsDelta==1 &&
            Math.Abs(closed.NextActivation.ProxyScore+.1)<1e-12,
            "next activation records intervening drift, not causal reward");
        Check(first.NextActivation==null,
            "previous episode export stays immutable when delayed outcome arrives");
        memory.Record(next,action,new ActionResult(false,"no-resource"),next,24);
        memory.BeginActivation(next);
        Check(memory.Inspect(original.Id)[1].Accepted==false &&
            memory.Inspect(original.Id)[1].Immediate.ProxyScore==0,
            "rejected action records zero immediate physical change");
        memory.Record(next,action,new ActionResult(true,"accepted"),changed,48);
        var capped=memory.Inspect(original.Id);
        Check(memory.TotalRecorded==3 && memory.RetainedEpisodes==2 &&
            capped.Count==2 && capped[0].Tick==24 && capped[1].Tick==48 &&
            memory.Inspect("different-actor").Count==0 &&
            memory.InspectAll().All(x=>x.AgentId==original.Id),
            "per-agent bounded retention and private actor-scoped inspection");
        bool rejectedCap=false;
        try{_ = new EpisodicOutcomeMemory(0);}catch(ArgumentOutOfRangeException){rejectedCap=true;}
        Check(rejectedCap,"invalid journal capacity rejected");

        EngineResults RunWithEpisodes(int seed,bool enabled){
            var sim=new SurvivalWorld(domain,new SurvivalConfig(seed,24,6,3,.65));
            return new Engine(sim,domain,host,
                new EngineOptions(TrackActionEpisodes:enabled,
                    MaxActionEpisodesPerAgent:3)).Run();
        }
        foreach(int seed in new[]{44,83,122}){
            var baseline=RunWithEpisodes(seed,false);
            var tracked=RunWithEpisodes(seed,true);
            var repeat=RunWithEpisodes(seed,true);
            Check(baseline.TraceHash==tracked.TraceHash &&
                baseline.World.Events==tracked.World.Events &&
                baseline.World.ResourcesBalanced && tracked.World.ResourcesBalanced &&
                tracked.TraceHash==repeat.TraceHash,
                "stage-13 observation-only integration preserves world and action parity seed="+seed);
            Check(baseline.TotalActionEpisodes==0 && baseline.ActionEpisodes.Count==0 &&
                tracked.TotalActionEpisodes>0 &&
                tracked.ActionEpisodes.Count<=24*3 &&
                tracked.ActionEpisodes.All(x=>x.Immediate.ProxyScore is >=-10 and <=10) &&
                JsonSerializer.Serialize(tracked.ActionEpisodes)==
                    JsonSerializer.Serialize(repeat.ActionEpisodes),
                "bounded deterministic actor-private episode history seed="+seed);
        }
    }

    // Controlled A/B output; not a claim of more intelligent decisions.
    static int CompareEpisodes(DomainRegistry domain,ModuleHost host){
        foreach(int seed in new[]{44,83,122}){
            EngineResults Run(bool tracked){
                var sim=new SurvivalWorld(domain,new SurvivalConfig(seed,24,6,3,.65));
                return new Engine(sim,domain,host,new EngineOptions(
                    TrackActionEpisodes:tracked,MaxActionEpisodesPerAgent:6)).Run();
            }
            var baseline=Run(false);
            var logged=Run(true);
            if(baseline.TraceHash!=logged.TraceHash ||
                !baseline.World.ResourcesBalanced||!logged.World.ResourcesBalanced)
                throw new InvalidOperationException("Logging changed world action trace or resources");
            Console.WriteLine(JsonSerializer.Serialize(new {
                seed,agents=24,days=6,loggingDoesNotAlterActions=true,
                recorded=logged.TotalActionEpisodes,retained=logged.ActionEpisodes.Count,
                completedFollowups=logged.ActionEpisodes.Count(x=>x.NextActivation!=null),
                meanImmediateProxy=logged.ActionEpisodes.Count==0?0:
                    logged.ActionEpisodes.Average(x=>x.Immediate.ProxyScore),
                limitations="Descriptive diagnostics, not causal reward or policy learning."
            }));
        }
        return 0;
    }

    static int CompareAdaptiveLearning(DomainRegistry domain,ModuleHost host){
        var rows=new List<object>();
        foreach(int seed in new[]{41,64,87,110,133,156,179,202}){
            EngineResults RunMode(bool learning){
                var world=new SurvivalWorld(domain,new SurvivalConfig(seed,24,12,3,.65));
                return new Engine(world,domain,host,new EngineOptions(
                    AdaptiveActionPolicy:learning,
                    MaxExperiencePreferencesPerAgent:24,
                    MaxActionEpisodesPerAgent:8)).Run();
            }
            var baseline=RunMode(false);
            var learned=RunMode(true);
            var replay=RunMode(true);
            if(!baseline.World.ResourcesBalanced||!learned.World.ResourcesBalanced||
               learned.TraceHash!=replay.TraceHash)
                throw new InvalidOperationException("Adaptive A/B invariant or replay failed");
            rows.Add(new{
                seed,
                baselineHealth=baseline.World.AverageHealth,
                adaptedHealth=learned.World.AverageHealth,
                baselineAlive=baseline.World.Alive,
                adaptedAlive=learned.World.Alive,
                baselineRejected=baseline.World.Rejected,
                adaptedRejected=learned.World.Rejected,
                differentTrace=baseline.TraceHash!=learned.TraceHash,
                experienceUpdates=learned.ExperienceUpdates,
                learnedEntries=learned.RetainedExperiencePreferences
            });
        }
        Console.WriteLine(JsonSerializer.Serialize(new{
            disclaimer="Paired synthetic seeds; immediate proxy is heuristic, no held-out policy validation or causal/general improvement claim.",
            agents=24,days=12,rows
        },new JsonSerializerOptions{WriteIndented=true}));
        return 0;
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
            TestEpisodicOutcomes(d,host);
            TestAdaptiveExperiencePolicy(d,host);
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

            // Multi-day stage-12 research journal: observations must be private,
            // replayable, bounded, and independent from normal NPC decisions.
            var journalMind=new AgentMind();
            var journalWorld=new SurvivalWorld(d,new SurvivalConfig(81,24,6));
            var journalActor=journalWorld.GetPrivateAgent(0);
            var journal=new PrivateBeliefTimeline(maxRevisionsPerAgent:3);
            void FeedBelief(long when,params Message[] events){
                var observed=new ObservationPacket(when,(int)(when/72)+1,.5,
                    new Dictionary<string,int>(),new Dictionary<string,bool>(),[],events);
                journalMind.Ingest(observed);
                journal.Observe(journalActor with { Evidence=journalMind.GetSnapshot().Evidence },observed);
            }
            FeedBelief(1,new Message("evidence","W1",Incident:"CASE-LONG",
                Root:"first",Suspect:"A",LogLikelihood:Math.Log(8),
                Source:"eyewitness",Tick:1));
            var firstRevision=journal.Inspect(journalActor.Id).Single();
            FeedBelief(24,new Message("evidence","W2",Incident:"CASE-LONG",
                Root:"first",Suspect:"A",LogLikelihood:Math.Log(8),
                Source:"hearsay",Tick:24));
            FeedBelief(25,new Message("retract","UNKNOWN",Root:"first",Tick:25));
            Check(journal.Inspect(journalActor.Id).Count==1,
                "repeated rumors and unauthorized retractions do not create belief revisions");
            FeedBelief(72,new Message("evidence","W3",Incident:"CASE-LONG",
                Root:"second",Suspect:"B",LogLikelihood:Math.Log(4),
                Source:"eyewitness",Tick:72));
            var dual=journal.Inspect(journalActor.Id).Last();
            Check(dual.Alternatives.Count==2 &&
                Math.Abs(dual.UnknownShare+dual.Alternatives.Sum(h=>h.Share)-1)<1e-12,
                "longitudinal private beliefs normalize competing hypotheses");
            FeedBelief(144,new Message("evidence","W4",Incident:"CASE-LONG",
                Root:"third",Suspect:"B",LogLikelihood:Math.Log(16),
                Source:"eyewitness",Tick:144));
            var revised=journal.Inspect(journalActor.Id).Last();
            Check(revised.Alternatives.First().Suspect=="B" &&
                revised.Alternatives.First(h=>h.Suspect=="B").Share>
                dual.Alternatives.First(h=>h.Suspect=="B").Share,
                "new independent source changes leading multi-day hypothesis");
            FeedBelief(216,new Message("retract","W1",Root:"first",Tick:216));
            var journalAfterCorrection=journal.Inspect(journalActor.Id);
            Check(journalAfterCorrection.Count==3 && journalAfterCorrection[0].Tick==72 &&
                journalAfterCorrection.Last().Alternatives.All(h=>h.Suspect!="A"),
                "authorized correction updates private belief and bounds retained journal");
            Check(journal.Inspect("unrelated-agent").Count==0 &&
                journal.InspectAll().Count==3,
                "research journal restricts actor-private inspection and exports retained entries");

            EngineResults RunTimelineScenario(bool enabled){
                var observedWorld=new SurvivalWorld(d,new SurvivalConfig(78,24,6,3,.65));
                observedWorld.ScheduleObservation(1,0,new Message("evidence","W1",
                    Incident:"CASE-RUNTIME",Root:"runtime-a",Suspect:"A003",
                    LogLikelihood:Math.Log(12),Source:"eyewitness",Tick:1));
                observedWorld.ScheduleObservation(25,0,new Message("evidence","W2",
                    Incident:"CASE-RUNTIME",Root:"runtime-b",Suspect:"A004",
                    LogLikelihood:Math.Log(8),Source:"eyewitness",Tick:25));
                observedWorld.ScheduleObservation(49,0,new Message("retract","W1",
                    Root:"runtime-a",Tick:49));
                return new Engine(observedWorld,d,host,new EngineOptions(
                    TrackPrivateBeliefs:enabled,MaxBeliefRevisionsPerAgent:8)).Run();
            }
            var runtimeBaseline=RunTimelineScenario(false);
            var runtimeTracked=RunTimelineScenario(true);
            var runtimeRepeat=RunTimelineScenario(true);
            Check(runtimeTracked.TraceHash==runtimeBaseline.TraceHash &&
                runtimeTracked.World.ResourcesBalanced &&
                runtimeTracked.World.Events==runtimeBaseline.World.Events,
                "opt-in longitudinal logging leaves baseline NPC decisions and world unchanged");
            Check(runtimeBaseline.BeliefRevisions.Count==0 &&
                runtimeTracked.BeliefRevisions.Any(e=>e.Incident=="CASE-RUNTIME") &&
                JsonSerializer.Serialize(runtimeTracked.BeliefRevisions)==
                    JsonSerializer.Serialize(runtimeRepeat.BeliefRevisions),
                "engine captures deterministic multi-day private evidence revisions");
            Check(firstRevision.Alternatives.Single().Suspect=="A",
                "belief journal first private observation captured correctly");

            // Stage 12: opt-in social action choice must distinguish one strong
            // rumor from two independent corroborating witnesses.
            var policyActor=journalActor with {
                Inventory=new Dictionary<string,int>(),
                Needs=new Dictionary<string,double>(),
                Personality=new Traits(0,0,0,0,0,0),
                PlanGoal=null,Requests=[],Nearby=[],
                Evidence=[],UseCompetingBeliefs=true,
                Acted=new HashSet<string>(StringComparer.Ordinal),
                Reported=new HashSet<string>(StringComparer.Ordinal),
                PendingCorrections=[],
                Debts=new Dictionary<string,int>()
            };
            var policyObservation=new ObservationPacket(30,1,.5,
                new Dictionary<string,int>(),new Dictionary<string,bool>(),[],[]);
            var socialContext=new SocialPolicyTestContext();
            var policyBehavior=new SurvivalBehavior();
            var witnessOne=new PrivateEvidence("claim-1","CASE-POLICY","A002",
                Math.Log(25),"eyewitness","W1",5);
            var witnessTwo=new PrivateEvidence("claim-2","CASE-POLICY","A002",
                Math.Log(16),"eyewitness","W2",6);
            var opposing=new PrivateEvidence("claim-opposing","CASE-POLICY","A003",
                Math.Log(25),"eyewitness","W3",5);
            var withOne=policyActor with {Evidence=new[]{witnessOne}};
            var singlePolicy=CompetingBeliefPolicy.Evaluate(withOne,30);
            Check(singlePolicy!=null&&!singlePolicy.MayAccuse &&
                policyBehavior.Choose(withOne,policyObservation,socialContext)
                    .Proposal.Kind=="survival:investigate",
                "one strong witness leads to inquiry, not direct accusation");
            var oldSocial=policyBehavior.Choose(withOne with {UseCompetingBeliefs=false},
                policyObservation,socialContext);
            Check(oldSocial.Proposal.Kind=="survival:accuse",
                "legacy one-witness accusation remains available only when policy disabled");
            var corroborated=policyActor with {Evidence=new[]{witnessOne,witnessTwo}};
            var twoPolicy=CompetingBeliefPolicy.Evaluate(corroborated,30);
            Check(twoPolicy!=null&&twoPolicy.MayAccuse &&
                policyBehavior.Choose(corroborated,policyObservation,socialContext)
                    .Proposal.Kind=="survival:accuse",
                "independent corroboration enables higher-confidence accusation");
            var disputed=policyActor with {Evidence=new[]{witnessOne,opposing}};
            var disputedPolicy=CompetingBeliefPolicy.Evaluate(disputed,30);
            Check(disputedPolicy!=null&&!disputedPolicy.MayAccuse &&
                !disputedPolicy.MayTell &&
                policyBehavior.Choose(disputed,policyObservation,socialContext)
                    .Proposal.Kind=="survival:investigate",
                "competing plausible suspects cause inquiry rather than accusation");
            var repeatedRoot=policyActor with {
                Evidence=new[]{
                    witnessOne,witnessOne with { Sender="OTHER",Source="hearsay" }
                }
            };
            Check(CompetingBeliefPolicy.Evaluate(repeatedRoot,30)?.MayAccuse==false,
                "forwarding the same root does not manufacture independent corroboration");
            var withdrawn=policyActor with {
                Evidence=new[]{witnessOne with {Active=false},witnessTwo}
            };
            Check(CompetingBeliefPolicy.Evaluate(withdrawn,30)?.MayAccuse==false,
                "authorized retraction withdraws accusation permission");
            var abstain=policyActor with {Evidence=[]};
            Check(CompetingBeliefPolicy.Evaluate(abstain,30)==null &&
                policyBehavior.Choose(abstain,policyObservation,socialContext)
                    .Proposal.Kind!="survival:accuse",
                "no private evidence produces no social accusation");

            EngineResults RunBeliefChoiceScenario(bool enabled){
                var socialWorld=new SurvivalWorld(d,
                    new SurvivalConfig(87,24,6,3,.65));
                socialWorld.ScheduleObservation(1,0,new Message("evidence","W1",
                    Incident:"CASE-CHOICE",Root:"first-choice",Suspect:"A002",
                    LogLikelihood:Math.Log(25),Source:"eyewitness",Tick:1));
                socialWorld.ScheduleObservation(25,0,new Message("evidence","W2",
                    Incident:"CASE-CHOICE",Root:"second-choice",Suspect:"A003",
                    LogLikelihood:Math.Log(25),Source:"eyewitness",Tick:25));
                socialWorld.ScheduleObservation(73,0,new Message("retract","W1",
                    Root:"first-choice",Tick:73));
                return new Engine(socialWorld,d,host,
                    new EngineOptions(CompetingBeliefPolicy:enabled)).Run();
            }
            var choiceBaseline=RunBeliefChoiceScenario(false);
            var choiceOptIn=RunBeliefChoiceScenario(true);
            var choiceReplay=RunBeliefChoiceScenario(true);
            Check(choiceOptIn.TraceHash==choiceReplay.TraceHash &&
                choiceOptIn.World.ResourcesBalanced && choiceBaseline.World.ResourcesBalanced &&
                choiceOptIn.World.Turns==choiceBaseline.World.Turns,
                "opt-in social cognition preserves deterministic replay and world conservation");

            // Stage 12: only the trusted World Authority knows whether an
            // accepted accusation matched the recorded incident actor.
            var verdictWorld=new SurvivalWorld(d,new SurvivalConfig(91,24,4,
                VerifiedAccusationFeedback:true,Visibility:0));
            var incidentAction=verdictWorld.Execute(new ActionProposal("A002",
                "survival:take",Item:"survival:water"));
            Check(incidentAction.Accepted && incidentAction.Event?.Incident!=null,
                "fixture creates an authoritative incident with a known actor");
            string verifiedIncident=incidentAction.Event!.Incident!;
            var firstVerdict=verdictWorld.Execute(new ActionProposal("A001",
                "survival:accuse",Target:"A003",Incident:verifiedIncident));
            Check(firstVerdict.Accepted&&firstVerdict.VerifiedOutcome?.Incident==verifiedIncident &&
                firstVerdict.VerifiedOutcome.ActualActor=="A002",
                "private post-accusation feedback identifies actual actor even after incorrect accusation");
            var inventedVerdict=verdictWorld.Execute(new ActionProposal("A001",
                "survival:accuse",Target:"A003",Incident:"CASE-NOT-IN-WORLD"));
            Check(inventedVerdict.Accepted&&inventedVerdict.VerifiedOutcome==null,
                "unknown incident receives no authoritative verification");
            var legacyVerdictWorld=new SurvivalWorld(d,
                new SurvivalConfig(91,24,4,Visibility:0));
            var legacyIncident=legacyVerdictWorld.Execute(new ActionProposal("A002",
                "survival:take",Item:"survival:water"));
            var legacyVerdict=legacyVerdictWorld.Execute(new ActionProposal("A001",
                "survival:accuse",Target:"A003",Incident:legacyIncident.Event!.Incident));
            Check(legacyVerdict.VerifiedOutcome==null,
                "default world does not reveal hidden incident truth");

            var feedbackMind=new AgentMind();
            var feedbackPacket=new ObservationPacket(1,1,.5,
                new Dictionary<string,int>(),new Dictionary<string,bool>(),[],
                [new Message("evidence","W1",Incident:verifiedIncident,Root:"w1-primary",
                    Suspect:"A002",LogLikelihood:Math.Log(9),Source:"eyewitness",Tick:1),
                 new Message("evidence","W1",Incident:verifiedIncident,Root:"w1-forwarded",
                    Suspect:"A002",LogLikelihood:Math.Log(4),Source:"hearsay",Tick:1),
                 new Message("evidence","W2",Incident:verifiedIncident,Root:"w2-false",
                    Suspect:"A003",LogLikelihood:Math.Log(9),Source:"eyewitness",Tick:1),
                 new Message("evidence","W3",Incident:verifiedIncident,Root:"w3-retracted",
                    Suspect:"A003",LogLikelihood:Math.Log(9),Source:"eyewitness",Tick:1),
                 new Message("evidence","W4",Incident:verifiedIncident,Root:"w4-a",
                    Suspect:"A002",LogLikelihood:Math.Log(9),Source:"eyewitness",Tick:1),
                 new Message("evidence","W4",Incident:verifiedIncident,Root:"w4-b",
                    Suspect:"A003",LogLikelihood:Math.Log(9),Source:"eyewitness",Tick:1)]);
            feedbackMind.Ingest(feedbackPacket);
            feedbackMind.Ingest(feedbackPacket with {Tick=2,Messages=[
                new Message("retract","W3",Root:"w3-retracted",Tick:2)]});
            var oldMindSnapshot=feedbackMind.GetSnapshot();
            int learned=feedbackMind.LearnFromVerifiedOutcome("A001",firstVerdict.VerifiedOutcome!);
            var learnedSnapshot=feedbackMind.GetSnapshot();
            Check(learned==2&&feedbackMind.Trust.GetValueOrDefault("W1")==8&&
                feedbackMind.Trust.GetValueOrDefault("W2")==-12&&
                feedbackMind.Trust.GetValueOrDefault("W3")==-4&&
                !feedbackMind.Trust.ContainsKey("W4"),
                "source trust rewards correct reports, penalizes false ones and skips withdrawn or contradictory senders");
            Check(feedbackMind.LearnFromVerifiedOutcome("A001",firstVerdict.VerifiedOutcome!)==0 &&
                oldMindSnapshot.Trust.Count==1 &&
                oldMindSnapshot.Trust.GetValueOrDefault("W3")==-4 &&
                learnedSnapshot.Trust.Count==3,
                "same verified incident cannot repeatedly increase trust and old snapshot is immutable");
            feedbackMind.Ingest(feedbackPacket with {Tick=3,Messages=[
                new Message("evidence","W1",Incident:"CASE-FUTURE",Root:"later-true",
                    Suspect:"A002",LogLikelihood:Math.Log(10),Source:"hearsay",Tick:3),
                new Message("evidence","W2",Incident:"CASE-FUTURE",Root:"later-false",
                    Suspect:"A003",LogLikelihood:Math.Log(10),Source:"hearsay",Tick:3)]});
            Check(feedbackMind.Evidence["later-true"].LogLikelihood>
                feedbackMind.Evidence["later-false"].LogLikelihood,
                "previous verified outcomes alter reliability of subsequent hearsay");

            (EngineResults Run, VerifiedTrustProbeDecision Probe) RunTrustScenario(
                bool engineLearning,bool worldOracle) {
                var scenarioWorld=new SurvivalWorld(d,new SurvivalConfig(144,24,4,
                    Visibility:0,VerifiedAccusationFeedback:worldOracle));
                var culpritAction=scenarioWorld.Execute(new ActionProposal("A002",
                    "survival:take",Item:"survival:water"));
                string caseId=culpritAction.Event!.Incident!;
                scenarioWorld.ScheduleObservation(1,0,new Message("evidence","W1",
                    Incident:caseId,Root:"first-trust",Suspect:"A002",
                    LogLikelihood:Math.Log(12),Source:"eyewitness",Tick:1));
                scenarioWorld.ScheduleObservation(1,0,new Message("evidence","W2",
                    Incident:caseId,Root:"second-trust",Suspect:"A003",
                    LogLikelihood:Math.Log(12),Source:"eyewitness",Tick:1));
                scenarioWorld.ScheduleObservation(25,0,new Message("evidence","W1",
                    Incident:"CASE-FUTURE",Root:"future-claim",Suspect:"A004",
                    LogLikelihood:Math.Log(10),Source:"hearsay",Tick:25));
                var probe=new VerifiedTrustProbeDecision(caseId);
                var probeHost=new ModuleHost();
                probeHost.AddDecision(probe);
                var result=new Engine(scenarioWorld,d,probeHost,
                    new EngineOptions(AdaptiveSourceTrust:engineLearning)).Run();
                return (result,probe);
            }
            var trustBaseline=RunTrustScenario(false,true);
            var trustEnabled=RunTrustScenario(true,true);
            var trustWithoutVerdict=RunTrustScenario(true,false);
            var trustReplay=RunTrustScenario(true,true);
            Check(trustBaseline.Run.TraceHash==trustEnabled.Run.TraceHash &&
                trustWithoutVerdict.Run.TraceHash==trustBaseline.Run.TraceHash &&
                trustEnabled.Run.TraceHash==trustReplay.Run.TraceHash &&
                trustEnabled.Run.World.ResourcesBalanced && trustBaseline.Run.World.ResourcesBalanced,
                "verified source learning preserves deterministic world actions and conservation");
            Check(trustBaseline.Run.VerifiedSourceTrustUpdates==0 &&
                trustWithoutVerdict.Run.VerifiedSourceTrustUpdates==0 &&
                trustEnabled.Run.VerifiedSourceTrustUpdates==2 &&
                trustEnabled.Probe.TrustInSource==8 &&
                trustBaseline.Probe.TrustInSource==0 &&
                trustWithoutVerdict.Probe.TrustInSource==0 &&
                trustEnabled.Probe.HearsayStrength>trustBaseline.Probe.HearsayStrength &&
                trustEnabled.Probe.HearsayStrength==trustReplay.Probe.HearsayStrength,
                "trusted outcome is actor-private, opt-in, deterministic and changes future hearsay weighting");




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

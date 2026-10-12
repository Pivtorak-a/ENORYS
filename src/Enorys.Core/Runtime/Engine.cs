using System.Security.Cryptography;
using System.Text;
using Enorys.Abstractions;
using Enorys.Core.Domain;
using Enorys.Core.Modules;
using Enorys.Core.Planning;
using Enorys.Core.Social;
namespace Enorys.Core.Runtime;

public sealed record EngineOptions(bool Planning=true,bool Social=true,bool CognitiveLod=true,bool AuditLod=false,bool EventScheduling=false);
public sealed record EngineResults(WorldMetrics World,long Automatic,long Deliberate,long CandidateChecks,
    int PlansCompleted,int PlansAbandoned,int Divergences,long ElapsedDecisionTicks,
    string TraceHash,IReadOnlyList<string> History){public long SkippedDecisions {get;init;} public long WakeInterruptions {get;init;}}
/// <summary>Core owns cognition, World Adapter owns truth and material state.</summary>
public sealed class Engine {
    private readonly IWorldAdapter world;
    private readonly AgentMind[] minds;
    private readonly Planner planner;
    private readonly IDecisionModule decision;
    private readonly EngineOptions options;
    private readonly WakeScheduler? scheduler;
    private readonly IWakePolicy? wakePolicy;
    private long automatic,deliberate,candidateChecks,decisionTicks;
    private int plansCompleted,plansAbandoned,divergences;
    private readonly List<string> history=new();

    private sealed class Context(Engine engine,AgentMind mind): IDecisionContext {
        public bool PlanningEnabled=>engine.options.Planning;
        public bool SocialEnabled=>engine.options.Social;
        public bool LodEnabled=>engine.options.CognitiveLod;
        public bool AuditLod=>engine.options.AuditLod;
        public ActionProposal? PlannedStep(AgentView a,ObservationPacket obs){
            if(!PlanningEnabled)return null;
            if(mind.Plan is { Finished:true })mind.Plan=null;
            if(mind.Plan==null){
                foreach(var goal in engine.plannerDomain.Goals.Values.OrderBy(g=>g.Id,StringComparer.Ordinal)){
                    if(a.Needs.GetValueOrDefault(goal.NeedId)<goal.Threshold||
                       a.Inventory.GetValueOrDefault(goal.ItemId)>0)continue;
                    var possible=engine.planner.MakePlan(a,obs,goal);
                    if(possible!=null){mind.Plan=possible;break;}
                }
            }
            return engine.planner.Next(a,obs,mind.Plan);
        }
        public bool CanSpare(AgentView a,string item)=>
            engine.planner.Unreserved(a,mind.Plan,item)>0;
    }
    private readonly DomainRegistry plannerDomain;
    public Engine(IWorldAdapter world,DomainRegistry domain,ModuleHost host,EngineOptions? opts=null){
        this.world=world;plannerDomain=domain;options=opts??new();
        planner=new Planner(domain);
        decision=host.Decisions.FirstOrDefault()??throw new InvalidOperationException("No decision module installed");
        minds=Enumerable.Range(0,world.AgentCount).Select(_=>new AgentMind()).ToArray();
        wakePolicy=host.WakePolicies.FirstOrDefault();
        if(options.EventScheduling && wakePolicy!=null)scheduler=new WakeScheduler(world.AgentCount);
    }
    private AgentView PrivateSnapshot(AgentView baseView,ObservationPacket packet,AgentMind mind) {
        var evidence=mind.Evidence.Values.ToArray();
        return baseView with {
            Evidence=evidence,
            Trust=new Dictionary<string,double>(mind.Trust),
            Debts=new Dictionary<string,int>(mind.Debts),
            Requests=mind.Requests.ToArray(),
            Nearby=packet.Nearby,
            Acted=new HashSet<string>(mind.Acted),
            Reported=new HashSet<string>(mind.Reported),
            PendingCorrections=mind.Corrections.Select(x=>new RetractionRequest(x.Root,x.To)).ToArray(),
            PlanGoal=mind.Plan?.Goal
        };
    }
    public EngineResults Run(){
        
        while(world.Tick<world.TotalTurns){
            int index=(int)(world.Tick%world.AgentCount);
            if(!world.IsAlive(index)){
                history.Add($"{world.Tick}|inactive");
                world.Advance();continue;
            }
            if(scheduler!=null){
                bool pending=world is IObservationSignal signal && signal.HasPendingObservation(index);
                if(!scheduler.ShouldRun(index,world.Tick,pending)){
                    history.Add($"{world.Tick}|deferred");
                    world.Advance();continue;
                }
            }
            var mind=minds[index];
            var obs=world.Observe(index);
            mind.Ingest(obs);
            var actor=PrivateSnapshot(world.GetPrivateAgent(index),obs,mind);
            var ctx=new Context(this,mind);
            var before=System.Diagnostics.Stopwatch.GetTimestamp();
            Enorys.Abstractions.Decision choice;
            if(options.CognitiveLod&&decision is IFastDecisionModule fast
                &&mind.Plan!=null && mind.LastInboxTick!=obs.Tick){
                var maybe=fast.TryFastDecision(actor,obs,ctx);
                if(maybe!=null){
                    if(options.AuditLod){
                        // Shadow scores may alter the plan during deliberation,
                        // but must never write the authoritative World.
                        var shadow=decision.Choose(actor,obs,ctx);
                        if(shadow.Proposal!=maybe.Proposal)divergences++;
                    }
                    choice=maybe;
                    automatic++;
                }else {choice=decision.Choose(actor,obs,ctx);deliberate++; }
            }else {choice=decision.Choose(actor,obs,ctx);deliberate++;}
            decisionTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-before;
            candidateChecks+=choice.CandidateCount;
            var result=world.Execute(choice.Proposal);
            if(result.Accepted){
                mind.OnAccepted(choice.Proposal,obs.Day,obs.Tick);
                if(mind.Plan is { } p&&!p.Finished &&p.Steps[p.Index].Kind==choice.Proposal.Kind){
                    p.Index++;
                    if(p.Finished){mind.Plan=null;plansCompleted++;}
                }
            }else if(mind.Plan!=null){mind.Plan=null;plansAbandoned++;}
            history.Add($"{obs.Tick}|{actor.Id}|{choice.Proposal.Kind}|{choice.Proposal.Item}|{choice.Proposal.Target}|{result.Accepted}");
            if(scheduler!=null && wakePolicy!=null){
                int sleepTurns=wakePolicy.SleepTurns(actor,obs,choice,result);
                scheduler.Schedule(index,world.Tick,sleepTurns);
            }
            world.Advance();
        }

        var bytes=SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",history)));
        var wm=world.GetMetrics() with {CandidateChecks=candidateChecks,
            AutoDecisions=automatic,Deliberations=deliberate,PlansCompleted=plansCompleted};
        return new EngineResults(wm,automatic,deliberate,candidateChecks,plansCompleted,plansAbandoned,
            divergences,decisionTicks,Convert.ToHexString(bytes),history){
                SkippedDecisions=scheduler?.Skipped??0,
                WakeInterruptions=scheduler?.Interrupted??0
            };
    }
}

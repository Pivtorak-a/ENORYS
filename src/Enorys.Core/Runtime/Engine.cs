using System.Security.Cryptography;
using System.Text;
using Enorys.Abstractions;
using Enorys.Core.Domain;
using Enorys.Core.Modules;
using Enorys.Core.Planning;
using Enorys.Core.Social;
namespace Enorys.Core.Runtime;

public sealed record EngineOptions(bool Planning=true,bool Social=true,bool CognitiveLod=true,bool AuditLod=false,bool EventScheduling=false,
    int HybridActiveAgents=0,int BackgroundMaximumRounds=9,
    int BackgroundRoundsPerDay=3,bool CaptureHistory=true);
public sealed record EngineResults(WorldMetrics World,long Automatic,long Deliberate,long CandidateChecks,
    int PlansCompleted,int PlansAbandoned,int Divergences,long ElapsedDecisionTicks,
    string TraceHash,IReadOnlyList<string> History){public long SkippedDecisions {get;init;} public long WakeInterruptions {get;init;}
    public long BackgroundDecisions {get;init;} public long ActiveDecisions {get;init;}
    public long FastForwardCalls {get;init;} public long MessagesProcessed {get;init;}
    public long AffectMessagesProcessed {get;init;} public long MemoryEvidenceProcessed {get;init;}
    public int CalendarHighWater {get;init;}
}
/// <summary>Core owns cognition, World Adapter owns truth and material state.</summary>
public sealed class Engine {
    private readonly IWorldAdapter world;
    private readonly AgentMind[] minds;
    private readonly Planner planner;
    private readonly IDecisionModule decision;
    private readonly EngineOptions options;
    private readonly WakeScheduler? scheduler;
    private readonly IWakePolicy? wakePolicy;
    private readonly PulseCalendar? calendar;
    private readonly IBackgroundDeadlinePolicy? deadlinePolicy;
    private readonly List<int> dirtyBuffer=new();
    private long backgroundDecisions,activeDecisions,fastForwardCalls,messagesProcessed,affectMessagesProcessed,memoryEvidenceProcessed;
    private readonly IReadOnlyList<IAffectiveModule> affects;
    private long automatic,deliberate,candidateChecks,decisionTicks;
    private int plansCompleted,plansAbandoned,divergences;
    private readonly List<string> history=new();

    private sealed class Context(Engine engine,AgentMind mind): IDecisionContext {
        public bool PlanningEnabled=>engine.options.Planning;
        public bool SocialEnabled=>engine.options.Social;
        public bool LodEnabled=>engine.options.CognitiveLod;
        public bool AuditLod=>engine.options.AuditLod;
        public bool HasAffect=>engine.affects.Count>0;
        public double EmotionBias(AgentView agent,string actionKind,string? target,long tick){
            double sum=0;foreach(var module in engine.affects)sum+=module.ActionBias(agent,actionKind,target,tick);
            return Math.Clamp(sum,-1,1);
        }
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
        affects=host.AffectiveModules;
        if(options.EventScheduling && wakePolicy!=null)scheduler=new WakeScheduler(world.AgentCount);
        deadlinePolicy=host.BackgroundDeadline;
        if(options.HybridActiveAgents>0){
            if(options.EventScheduling||options.BackgroundRoundsPerDay is <1 or >24||
               options.HybridActiveAgents>=world.AgentCount||
               options.BackgroundMaximumRounds is <1 or >100 ||
               world is not IWorldFastForward || world is not IPrivateWakeFeed ||
               deadlinePolicy==null)
                throw new InvalidOperationException("Pulse hybrid requires fast-forward world, non-consuming private wake feed and a registered background deadline policy (and cannot combine with legacy EventScheduling)");
            calendar=new PulseCalendar(world.AgentCount,options.HybridActiveAgents);
        }
    }
    private AgentView PrivateSnapshot(AgentView baseView,ObservationPacket packet,AgentMind mind) {
        var state=mind.GetSnapshot();
        return baseView with {
            Evidence=state.Evidence,Trust=state.Trust,Debts=state.Debts,
            Requests=state.Requests,Nearby=packet.Nearby,Acted=state.Acted,
            Reported=state.Reported,PendingCorrections=state.Corrections,
            PlanGoal=mind.Plan?.Goal
        };
    }
    public EngineResults Run(){
        
        while(world.Tick<world.TotalTurns){
            int index=(int)(world.Tick%world.AgentCount);
            if(calendar!=null){
                var feed=(IPrivateWakeFeed)world;
                feed.DrainWakeSignals(dirtyBuffer);
                if(world is IObservationSignal signal){
                    foreach(int pending in dirtyBuffer)
                        if(signal.HasPendingObservation(pending))
                            calendar.InvalidateFromPrivateMessage(pending,world.Tick);
                }
                dirtyBuffer.Clear();
                if(index>=options.HybridActiveAgents){
                    long due=calendar.NextDue;
                    if(due>world.Tick){
                        // Never skip over an active actor slot: physics/day effects
                        // are advanced in the authoritative World Adapter.
                        int untilNextActive=world.AgentCount-index;
                        long untilEvent=world is IWorldScheduledStimuli schedule
                            ? Math.Max(0,schedule.NextExternalStimulusTick-world.Tick)
                            : long.MaxValue;
                        int slots=(int)Math.Min((long)untilNextActive,
                            Math.Min(untilEvent,Math.Min(due-world.Tick,world.TotalTurns-world.Tick)));
                        if(slots<=0)throw new InvalidOperationException("Pulse deadlock");
                        ((IWorldFastForward)world).AdvanceSlots(slots);
                        calendar.Skipped(slots);
                        fastForwardCalls++;
                        continue;
                    }
                    if(!calendar.ConsumeDue(index,world.Tick))
                        throw new InvalidOperationException("Invalid pulse deadline ordering");
                }
            }
            if(!world.IsAlive(index)){
                if(options.CaptureHistory)history.Add($"{world.Tick}|inactive");
                world.Advance();continue;
            }
            if(scheduler!=null){
                bool pending=world is IObservationSignal signal && signal.HasPendingObservation(index);
                if(!scheduler.ShouldRun(index,world.Tick,pending)){
                    if(options.CaptureHistory)history.Add($"{world.Tick}|deferred");
                    world.Advance();continue;
                }
            }
            var mind=minds[index];
            var obs=world.Observe(index);
            mind.Ingest(obs);
            messagesProcessed+=obs.Messages.Count;
            memoryEvidenceProcessed+=obs.Messages.Count(m=>m.Type is "evidence" or "retract");
            var actor=PrivateSnapshot(world.GetPrivateAgent(index),obs,mind);
            if(obs.Messages.Count>0)foreach(var affect in affects){
                affect.Receive(actor,obs);
                affectMessagesProcessed+=obs.Messages.Count;
            }
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
            if(calendar!=null){
                if(index<options.HybridActiveAgents)activeDecisions++;
                else backgroundDecisions++;
            }
            candidateChecks+=choice.CandidateCount;
            var result=world.Execute(choice.Proposal);
            if(result.Accepted){
                mind.OnAccepted(choice.Proposal,obs.Day,obs.Tick);
                foreach(var affect in affects)affect.ObserveOwnAction(actor,choice.Proposal,obs.Tick);
                if(mind.Plan is { } p&&!p.Finished &&p.Steps[p.Index].Kind==choice.Proposal.Kind){
                    p.Index++;
                    if(p.Finished){mind.Plan=null;plansCompleted++;}
                }
            }else if(mind.Plan!=null){mind.Plan=null;plansAbandoned++;}
            if(options.CaptureHistory)
                history.Add($"{obs.Tick}|{actor.Id}|{choice.Proposal.Kind}|{choice.Proposal.Item}|{choice.Proposal.Target}|{result.Accepted}");
            if(calendar!=null && index>=options.HybridActiveAgents && world.IsAlive(index)){
                int rounds=1;
                if(mind.Plan==null && !mind.Evidence.Values.Any(x=>x.Active) &&
                    mind.Requests.Count==0 && !mind.Debts.Any(x=>x.Value>0) &&
                    mind.Corrections.Count==0){
                    var updated=world.GetPrivateAgent(index);
                    rounds=deadlinePolicy!.NextWakeRounds(updated,obs,
                        options.BackgroundRoundsPerDay,
                        options.BackgroundMaximumRounds);
                }
                calendar.AfterTurn(index,world.Tick,rounds);
            }
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
                WakeInterruptions=(scheduler?.Interrupted??0)+(calendar?.Interrupted??0),
                SkippedDecisions=(scheduler?.Skipped??0)+(calendar?.SkippedSlots??0),
                BackgroundDecisions=backgroundDecisions,
                ActiveDecisions=activeDecisions,FastForwardCalls=fastForwardCalls,
                MessagesProcessed=messagesProcessed,
                AffectMessagesProcessed=affectMessagesProcessed,
                MemoryEvidenceProcessed=memoryEvidenceProcessed,
                CalendarHighWater=calendar?.HighWater??0
            };
    }
}

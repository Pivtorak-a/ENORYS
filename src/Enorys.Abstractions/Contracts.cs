using System.Collections.ObjectModel;
namespace Enorys.Abstractions;

/// <summary>All runtime identifiers are namespaced string IDs in the public API.</summary>
public record NeedDefinition(string Id, double Daily, double Weight = 1, double Initial = 25);
public record ItemDefinition(string Id, string Category);
public record RecipeDefinition(string Id, Dictionary<string,int> Inputs, Dictionary<string,int> Outputs);
public record GoalDefinition(string Id,string NeedId,string ItemId,string UseAction,double Threshold);
public record SourceDefinition(string ItemId,string ActionId);
public record ActionDefinition(string Id,string ItemId,string NeedId,double NeedDelta);
public record DomainPack(string Id,string Namespace,string Version,
    List<NeedDefinition> Needs,List<ItemDefinition> Items,
    List<RecipeDefinition> Recipes,List<GoalDefinition> Goals,
    List<ActionDefinition> Actions,List<SourceDefinition> Sources);

public record AgentView(string Id,int Zone,int Group,bool Alive,
    IReadOnlyDictionary<string,int> Inventory,IReadOnlyDictionary<string,double> Needs,
    Traits Personality,double Health,double Injury,double Infection,
    string? PlanGoal,IReadOnlyList<PrivateEvidence> Evidence,
    IReadOnlyDictionary<string,double> Trust,IReadOnlyDictionary<string,int> Debts,
    IReadOnlyList<string> Nearby,IReadOnlyList<HelpRequest> Requests,
    IReadOnlySet<string> Acted,IReadOnlySet<string> Reported,
    IReadOnlyList<RetractionRequest> PendingCorrections);

public record Traits(double Greed,double Generosity,double Norm,double Courage,double Curiosity,double Sociability);
public record PrivateEvidence(string Root,string Incident,string Suspect,double LogLikelihood,
    string Source,string Sender,long Tick,bool Active = true);
public record HelpRequest(string From,string Item,long Tick);
public record RetractionRequest(string Root,string Recipient);
public record ObservationPacket(long Tick,int Day,double Weather,
    IReadOnlyDictionary<string,int> Depot,IReadOnlyDictionary<string,bool> SourcesKnown,
    IReadOnlyList<string> Nearby,IReadOnlyList<Message> Messages);
public record Message(string Type,string From,string? Recipient=null,string? Item=null,
    string? Incident=null,string? Root=null,string? Suspect=null,double LogLikelihood=0,
    string? Source=null,long Tick=0);
public record ActionProposal(string Actor,string Kind,string? Item=null,string? Target=null,
    string? Incident=null,string? Root=null,string? Suspect=null,double LogLikelihood=0);
public record Candidate(ActionProposal Proposal,double Utility,string Reason);
public record Decision(ActionProposal Proposal,string Mode,string Reason,int CandidateCount,double Score);
public record ActionResult(bool Accepted,string Reason,WorldEvent? Event = null);
public record WorldEvent(long Tick,int Day,string Actor,string Kind,string? Item,string? Target,
    string? Incident,string? Root);
public record WorldMetrics(double AverageHealth,int Alive,int Turns,long CandidateChecks,
    long AutoDecisions,long Deliberations,long Rejected,int PlansCompleted,int Events,
    bool ResourcesBalanced,Dictionary<string,int> Actions);
public interface IWorldAdapter {
    int AgentCount { get; }
    int TotalTurns { get; }
    long Tick { get; }
    AgentView GetPrivateAgent(int index);
    ObservationPacket Observe(int index);
    ActionResult Execute(ActionProposal proposal);
    bool IsAlive(int index);
    void Advance();
    WorldMetrics GetMetrics();
}
public interface IDecisionModule {
    string ModuleId { get; }
    Decision Choose(AgentView agent,ObservationPacket observations,IDecisionContext context);
}
public interface IDecisionContext {
    ActionProposal? PlannedStep(AgentView agent,ObservationPacket packet);
    bool CanSpare(AgentView agent,string item);
    bool PlanningEnabled { get; }
    bool SocialEnabled { get; }
    bool LodEnabled { get; }
    bool AuditLod { get; }
}
public interface IFastDecisionModule {
    Decision? TryFastDecision(AgentView agent, ObservationPacket observations, IDecisionContext context);
}
public interface IEnorysModule {
    string Id { get; }
    void Register(IModuleRegistry registry);
}
public interface IModuleRegistry {
    void AddDecision(IDecisionModule module);
    void AddAppraisal(IAppraisalModule module);
}
public interface IAppraisalModule {
    string ModuleId { get; }
    double Evaluate(AgentView subject,PrivateEvidence evidence);
}

/** Optional world-owned signal: checking for inbox messages must not consume them. */
public interface IObservationSignal {
    bool HasPendingObservation(int agentIndex);
}
/** Optional domain wake policy. Delay is in this agent's own turns, not global ticks. */
public interface IWakePolicy {
    int SleepTurns(AgentView agent,ObservationPacket packet,Decision decision,ActionResult result);
}

using Enorys.Abstractions;

namespace Enorys.Core.Runtime;

/// <summary>
/// A small, actor-private set of observable state indicators. Inventory units
/// are an unweighted count, not an economic valuation of different goods.
/// NeedPressure is the arithmetic mean of available 0–100 needs.
/// </summary>
public sealed record ActorOutcomeState(double Health,double NeedPressure,long InventoryUnits) {
    public static ActorOutcomeState Capture(AgentView agent) {
        ArgumentNullException.ThrowIfNull(agent);
        if(!double.IsFinite(agent.Health))
            throw new ArgumentException("Non-finite actor health",nameof(agent));
        if(agent.Needs.Values.Any(n=>!double.IsFinite(n)))
            throw new ArgumentException("Non-finite actor needs",nameof(agent));
        double need=agent.Needs.Count==0?0:agent.Needs.Values.Average();
        long units=0;
        foreach(var count in agent.Inventory.Values)units=checked(units+count);
        return new ActorOutcomeState(agent.Health,need,units);
    }
}

/// <summary>
/// Observational change between snapshots. Positive NeedPressureDelta means
/// needs worsened. ProxyScore is a fixed diagnostic index, NOT causal credit,
/// a learned reward, or a calibrated measure of agent well-being.
/// </summary>
public sealed record EpisodeOutcome(
    double HealthDelta,double NeedPressureDelta,long InventoryUnitsDelta,double ProxyScore) {
    public static EpisodeOutcome Compare(ActorOutcomeState before,ActorOutcomeState after) {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        double dh=after.Health-before.Health;
        double dn=after.NeedPressure-before.NeedPressure;
        long di=checked(after.InventoryUnits-before.InventoryUnits);
        double proxy=Math.Clamp(.05*dh-.03*dn+.1*di,-10,10);
        return new EpisodeOutcome(dh,dn,di,proxy);
    }
}

/// <summary>
/// Immediate measures compare immediately-before and immediately-after states.
/// NextActivation compares that after-action state with the private state seen
/// when the SAME actor next gets a cognitive turn (possibly much later).
/// Neither delta establishes that the action caused the observed change.
/// </summary>
public sealed record ActionEpisode(string AgentId,long Tick,string ActionKind,
    string? Target,bool Accepted,string ResultReason,
    EpisodeOutcome Immediate,EpisodeOutcome? NextActivation);

/// <summary>
/// An opt-in, bounded journal. Retention is per agent, not global. Stores only
/// numeric private state summaries and action identifiers, never World Authority.
/// Logging is deliberately observational: the decision policy cannot read
/// these episodes and no action scores are altered.
/// </summary>
public sealed class EpisodicOutcomeMemory {
    private sealed class ActorHistory {
        public readonly List<ActionEpisode> Episodes=new();
        public ActorOutcomeState? PendingAfterAction;
    }

    private readonly Dictionary<string,ActorHistory> actors=new(StringComparer.Ordinal);
    private readonly int maxEpisodesPerActor;
    private long recorded;

    public EpisodicOutcomeMemory(int maxEpisodesPerActor=32){
        if(maxEpisodesPerActor is <1 or >2048)
            throw new ArgumentOutOfRangeException(nameof(maxEpisodesPerActor));
        this.maxEpisodesPerActor=maxEpisodesPerActor;
    }

    /// <summary>Close the last episode when this same actor next awakens.</summary>
    public void BeginActivation(AgentView current){
        ArgumentNullException.ThrowIfNull(current);
        if(!actors.TryGetValue(current.Id,out var actor)||
           actor.PendingAfterAction==null||actor.Episodes.Count==0)return;
        var entry=actor.Episodes[^1];
        if(entry.NextActivation!=null)
            throw new InvalidOperationException("Episode already closed");
        actor.Episodes[^1]=entry with {
            NextActivation=EpisodeOutcome.Compare(
                actor.PendingAfterAction,ActorOutcomeState.Capture(current))
        };
        actor.PendingAfterAction=null;
    }

    /// <summary>
    /// Call once, after Execute, for each actor turn. The after-state must
    /// belong to the same actor and come from that actor's private view.
    /// </summary>
    public void Record(AgentView before,ActionProposal proposed,
        ActionResult result,AgentView after,long tick){
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(after);
        if(tick<0||before.Id!=after.Id||before.Id!=proposed.Actor)
            throw new ArgumentException("Inconsistent actor or tick");
        if(!actors.TryGetValue(before.Id,out var actor)){
            actor=new ActorHistory();
            actors.Add(before.Id,actor);
        }
        if(actor.PendingAfterAction!=null)
            throw new InvalidOperationException("Previous episode was not closed");

        var afterState=ActorOutcomeState.Capture(after);
        var beforeState=ActorOutcomeState.Capture(before);
        var episode=new ActionEpisode(before.Id,tick,proposed.Kind,proposed.Target,
            result.Accepted,result.Reason,
            EpisodeOutcome.Compare(beforeState,afterState),null);
        actor.Episodes.Add(episode);
        actor.PendingAfterAction=afterState;
        recorded++;
        if(actor.Episodes.Count>maxEpisodesPerActor)
            actor.Episodes.RemoveAt(0);
    }

    /// <summary>Actor-scoped diagnostics, returned as an immutable snapshot.</summary>
    public IReadOnlyList<ActionEpisode> Inspect(string agentId){
        if(string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("Agent ID required",nameof(agentId));
        return actors.TryGetValue(agentId,out var actor)
            ?Array.AsReadOnly(actor.Episodes.ToArray())
            :Array.Empty<ActionEpisode>();
    }

    /// <summary>Privileged research export only; never expose to NPC decision modules.</summary>
    public IReadOnlyList<ActionEpisode> InspectAll()=>Array.AsReadOnly(
        actors.Values.SelectMany(x=>x.Episodes)
            .OrderBy(e=>e.Tick)
            .ThenBy(e=>e.AgentId,StringComparer.Ordinal).ToArray());

    public long TotalRecorded=>recorded;
    public long RetainedEpisodes=>actors.Values.Sum(a=>(long)a.Episodes.Count);
}

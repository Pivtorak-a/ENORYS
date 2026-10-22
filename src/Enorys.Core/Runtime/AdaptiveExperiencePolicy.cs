using Enorys.Abstractions;

namespace Enorys.Core.Runtime;

/// <summary>
/// An actor-private, tabular experimental preference learned from its own
/// immediately observed action outcomes. No counterfactual or causal inference.
/// </summary>
public sealed record ExperiencePreference(string Situation,string ActionKind,
    string? Item,double MeanSignal,int Samples,double UtilityBias);

/// <summary>
/// Bounded, opt-in online preference adaptation. Neither the feature key nor
/// the signal reads other agents' states or World Authority. The signal uses
/// Stage-13's immediate diagnostic proxy, NOT next-activation drift (which can
/// include unrelated events). Biases remain small versus survival priorities.
/// </summary>
public sealed class AdaptiveExperiencePolicy {
    private readonly record struct Key(string Situation,string Kind,string Item);
    private sealed class Estimate {
        public double Mean;
        public int Samples;
    }
    private sealed class History {
        public readonly Dictionary<Key,Estimate> Values=new();
        public readonly Queue<Key> InsertionOrder=new();
    }

    private readonly Dictionary<string,History> actors=new(StringComparer.Ordinal);
    private readonly int maxEntriesPerActor;
    private readonly double learningRate;
    private readonly double maximumUtilityBias;
    private long totalUpdates;

    public AdaptiveExperiencePolicy(int maxEntriesPerActor=48,
        double learningRate=.3,double maximumUtilityBias=.4){
        if(maxEntriesPerActor is <1 or >512)
            throw new ArgumentOutOfRangeException(nameof(maxEntriesPerActor));
        if(!double.IsFinite(learningRate)||learningRate<=0||learningRate>1)
            throw new ArgumentOutOfRangeException(nameof(learningRate));
        if(!double.IsFinite(maximumUtilityBias)||maximumUtilityBias<0||maximumUtilityBias>.5)
            throw new ArgumentOutOfRangeException(nameof(maximumUtilityBias));
        this.maxEntriesPerActor=maxEntriesPerActor;
        this.learningRate=learningRate;
        this.maximumUtilityBias=maximumUtilityBias;
    }

    /// <summary>
    /// State abstraction: highest private need and a coarse need-pressure bin.
    /// Unrelated states do not share preferences. Tie-breaking is ordinal.
    /// </summary>
    public static string Situation(AgentView actor){
        ArgumentNullException.ThrowIfNull(actor);
        string? highest=null;
        double pressure=double.NegativeInfinity;
        foreach(var need in actor.Needs){
            if(!double.IsFinite(need.Value))
                throw new ArgumentException("Nonfinite need",nameof(actor));
            if(need.Value>pressure || (need.Value==pressure &&
               (highest==null||StringComparer.Ordinal.Compare(need.Key,highest)<0))){
                pressure=need.Value;highest=need.Key;
            }
        }
        if(highest==null)return "none";
        return highest+"\u001f"+(pressure>=60?"high":pressure>=30?"medium":"low");
    }

    /// <summary>Only called with the *actual* episode recorded by EpisodicOutcomeMemory.</summary>
    public void Learn(AgentView before,ActionProposal choice,ActionEpisode episode){
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(episode);
        if(before.Id!=choice.Actor||before.Id!=episode.AgentId||
           choice.Kind!=episode.ActionKind||!double.IsFinite(episode.Immediate.ProxyScore))
            throw new ArgumentException("Experience must belong to this actor and action");
        string situation=Situation(before);
        var key=new Key(situation,choice.Kind,choice.Item??"");
        if(!actors.TryGetValue(before.Id,out var actor)){
            actor=new History();
            actors.Add(before.Id,actor);
        }
        if(!actor.Values.TryGetValue(key,out var value)){
            if(actor.Values.Count==maxEntriesPerActor){
                Key oldest=actor.InsertionOrder.Dequeue();
                actor.Values.Remove(oldest);
            }
            value=new Estimate();
            actor.Values.Add(key,value);
            actor.InsertionOrder.Enqueue(key);
        }

        // Rejected actions are a bounded feasibility penalty, not world reward.
        // This remains a *heuristic* immediate-only preference signal.
        double observation=episode.Accepted
            ?Math.Clamp(episode.Immediate.ProxyScore,-2,2):-1;
        value.Mean+=learningRate*(observation-value.Mean);
        if(value.Samples<int.MaxValue)value.Samples++;
        totalUpdates++;
    }

    /// <summary>
    /// Read-only actor-specific bias. At least 2 observations are required;
    /// unseen or unrelated action/state combinations return exactly zero.
    /// </summary>
    public double Bias(string agentId,string situation,ActionProposal choice){
        if(!actors.TryGetValue(agentId,out var actor))return 0;
        if(!actor.Values.TryGetValue(new Key(situation,choice.Kind,choice.Item??""),
            out var value)||value.Samples<2)return 0;
        return Math.Clamp(value.Mean*.35,-maximumUtilityBias,maximumUtilityBias);
    }

    public IReadOnlyList<ExperiencePreference> Inspect(string agentId){
        if(!actors.TryGetValue(agentId,out var actor))
            return Array.Empty<ExperiencePreference>();
        return Array.AsReadOnly(actor.Values
            .OrderBy(x=>x.Key.Situation,StringComparer.Ordinal)
            .ThenBy(x=>x.Key.Kind,StringComparer.Ordinal)
            .ThenBy(x=>x.Key.Item,StringComparer.Ordinal)
            .Select(x=>new ExperiencePreference(x.Key.Situation,x.Key.Kind,
                x.Key.Item.Length==0?null:x.Key.Item,x.Value.Mean,
                x.Value.Samples,
                x.Value.Samples<2?0:Math.Clamp(x.Value.Mean*.35,
                    -maximumUtilityBias,maximumUtilityBias))).ToArray());
    }

    public long TotalUpdates=>totalUpdates;
    public long RetainedPreferences=>actors.Values.Sum(x=>(long)x.Values.Count);
}

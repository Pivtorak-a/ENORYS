using System.Text.Json;
using Enorys.Abstractions;
namespace Enorys.Affect.ACM;

/// <summary>Experimental subjective graph: source-root -> target -> emotion. Not a validated psychology model.</summary>
public sealed record EmotionDefinition(string Id,double Decay,double Impulse,
    Dictionary<string,double> ActionBias);
public sealed record EmotionCatalog(string Namespace,string Version,List<EmotionDefinition> Emotions);
internal sealed record Episode(string Kind,string Target,string Root,double Initial,long Tick);

public sealed class AffectiveMap : IEnorysModule, IAffectiveModule {
    private readonly Dictionary<string,EmotionDefinition> definitions;
    private readonly Dictionary<string,Dictionary<(string Root,string Kind,string Target),Episode>> state=new(StringComparer.Ordinal);
    private readonly HashSet<string> seen=new(StringComparer.Ordinal);
    public string Id=>"enorys.affect:acm";
    public string ModuleId=>Id;
    public AffectiveMap(string jsonPath){
        var catalog=JsonSerializer.Deserialize<EmotionCatalog>(File.ReadAllText(jsonPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive=true })
            ??throw new InvalidDataException("Missing affect catalog");
        if(string.IsNullOrWhiteSpace(catalog.Namespace)||catalog.Emotions.Count==0)
            throw new InvalidDataException("Empty affect catalog");
        definitions=new Dictionary<string,EmotionDefinition>(StringComparer.Ordinal);
        foreach(var emotion in catalog.Emotions){
            var id=emotion.Id.Contains(':')?emotion.Id:catalog.Namespace+":"+emotion.Id;
            if(emotion.Decay<0||emotion.Decay>2||!double.IsFinite(emotion.Decay)||
                emotion.Impulse<0||emotion.Impulse>1||!double.IsFinite(emotion.Impulse)||
                emotion.ActionBias.Any(x=>!double.IsFinite(x.Value))||
                !definitions.TryAdd(id,emotion with {Id=id}))
                throw new InvalidDataException("Invalid or duplicate emotion "+id);
        }
    }
    public void Register(IModuleRegistry registry)=>registry.AddAffectiveModule(this);

    public void Receive(AgentView agent,ObservationPacket observation){
        if(observation.Messages.Count==0)return;
        foreach(var m in observation.Messages){
            string root=m.Root??m.From+"/"+m.Type+"/"+m.Tick+"/"+m.Item;
            var dedup=agent.Id+"/"+m.Type+"/"+m.From+"/"+root;
            if(!seen.Add(dedup))continue; // Retelling an identical source root cannot yield another emotional impulse.
            switch(m.Type){
                case "evidence":
                    if(m.Suspect==null)break;
                    double norm=agent.Personality.Norm, courage=agent.Personality.Courage;
                    double reliability=m.Source=="eyewitness"?1:m.Source=="trace"?.8:.45;
                    Impulse(agent.Id,"anger",m.Suspect,root,observation.Tick,
                        reliability*norm*(1/(1+Math.Exp(-m.LogLikelihood)))*.7);
                    Impulse(agent.Id,"fear",m.Suspect,root,observation.Tick,
                        reliability*(1-courage)*.35);
                    break;
                case "gift":
                    Impulse(agent.Id,"gratitude",m.From,root,observation.Tick,.7*agent.Personality.Generosity+.15);
                    break;
                case "accused":
                    Impulse(agent.Id,"sadness",m.From,root,observation.Tick,.25+agent.Personality.Norm*.3);
                    Impulse(agent.Id,"anger",m.From,root,observation.Tick,.2+agent.Personality.Norm*.4);
                    break;
                case "retract":
                    // A correction is evidence *about the provenance*, not instant forgiveness.
                    if(m.Root==null)break;
                    if(state.TryGetValue(agent.Id,out var graph)){
                        foreach(var (key,episode) in graph.ToArray()){
                            if(key.Root==m.Root && key.Kind.EndsWith(":anger",StringComparison.Ordinal)){
                                var decayed=Intensity(episode,observation.Tick);
                                graph[key]=episode with {Initial=decayed*.4,Tick=observation.Tick};
                            }
                        }
                    }
                    break;
            }
        }
    }
    private void Impulse(string agent,string kind,string target,string root,long tick,double amount){
        var id=definitions.Keys.FirstOrDefault(x=>x.EndsWith(":"+kind,StringComparison.Ordinal));
        if(id==null)return;
        amount=Math.Clamp(amount*definitions[id].Impulse,0,1);
        if(amount<=0)return;
        if(!state.TryGetValue(agent,out var graph)){graph=new();state.Add(agent,graph);}
        var key=(root,id,target);
        // Root+emotion+target uniquely identify a subjective appraisal; no echo multiplier.
        if(!graph.ContainsKey(key))graph.Add(key,new Episode(id,target,root,amount,tick));
    }
    private double Intensity(Episode e,long tick){
        if(tick<e.Tick)throw new ArgumentOutOfRangeException(nameof(tick));
        return e.Initial*Math.Exp(-definitions[e.Kind].Decay*(tick-e.Tick));
    }
    public IReadOnlyList<EmotionActivation> Inspect(string agentId,long tick){
        if(!state.TryGetValue(agentId,out var graph))return [];
        return graph.Values.OrderBy(x=>x.Kind,StringComparer.Ordinal)
            .ThenBy(x=>x.Target,StringComparer.Ordinal).ThenBy(x=>x.Root,StringComparer.Ordinal)
            .Select(e=>new EmotionActivation(e.Kind,e.Target,e.Root,Intensity(e,tick),tick)).ToArray();
    }
    public double ActionBias(AgentView agent,string actionKind,string? target,long tick){
        if(!state.TryGetValue(agent.Id,out var graph))return 0;
        double total=0;
        foreach(var episode in graph.Values){
            if(target!=null&&target!=episode.Target)continue;
            if(definitions[episode.Kind].ActionBias.TryGetValue(actionKind,out var b))
                total+=b*Intensity(episode,tick);
        }
        return Math.Clamp(total,-.6,.6);
    }
}

using System.Text.Json;
namespace Enorys.Cognition.Graph;

public sealed record CausalRule(string Id,string EventType,string Target,
    string Action,bool RequireEvidence,string? Affect,double Gain,double Decay);
public sealed record CausalRulePack(string Namespace,string Version,
    int MaxRootsPerAgent,int MaxDecisionsPerAgent,List<CausalRule> Rules);

public static class CausalRules {
    private static bool ValidId(string text)=>!string.IsNullOrWhiteSpace(text)
        &&text.All(c=>char.IsAsciiLetterOrDigit(c)||c is ':' or '-' or '_' or '.');
    public static (CausalRule[] Rules,int MaxRoots,int MaxDecisions) Load(params string[] paths){
        if(paths.Length==0)throw new InvalidDataException("At least one cognitive rule pack required");
        var all=new List<CausalRule>();
        var names=new HashSet<string>(StringComparer.Ordinal);
        int maxRoots=0,maxDecisions=0;
        foreach(var path in paths){
            var pack=JsonSerializer.Deserialize<CausalRulePack>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive=true })
                ??throw new InvalidDataException("Missing rule pack");
            if(!ValidId(pack.Namespace)||pack.Namespace.Contains(':',StringComparison.Ordinal)||
                !ValidId(pack.Version)||pack.Rules.Count==0||
                pack.MaxRootsPerAgent is <1 or >2048||
                pack.MaxDecisionsPerAgent is <1 or >2048)
                throw new InvalidDataException("Invalid causal rule pack");
            maxRoots=Math.Max(maxRoots,pack.MaxRootsPerAgent);
            maxDecisions=Math.Max(maxDecisions,pack.MaxDecisionsPerAgent);
            foreach(var rule in pack.Rules){
                var id=rule.Id.Contains(':',StringComparison.Ordinal)
                    ?rule.Id:pack.Namespace+":"+rule.Id;
                if(!ValidId(id)||!ValidId(rule.EventType)||rule.Target is not ("suspect" or "from")||
                   !ValidId(rule.Action)||(rule.Affect!=null&&!ValidId(rule.Affect))||
                   !double.IsFinite(rule.Gain)||rule.Gain<0||rule.Gain>1||
                   !double.IsFinite(rule.Decay)||rule.Decay<0||rule.Decay>1||
                   !names.Add(id))
                    throw new InvalidDataException("Invalid/duplicate causal rule "+id);
                all.Add(rule with {Id=id});
            }
        }
        // Stable rule ordering independent of JSON object iteration order.
        return (all.OrderBy(x=>x.Id,StringComparer.Ordinal).ToArray(),maxRoots,maxDecisions);
    }
}

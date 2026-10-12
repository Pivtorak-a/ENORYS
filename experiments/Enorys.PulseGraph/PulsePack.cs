using System.Text.Json;
namespace Enorys.PulseGraph;

public sealed record SignalDefinition(string Id,int Rate,int Threshold,int Relief,int CoupledDelta=0);
public sealed record SignalPack(string Namespace,string Version,List<SignalDefinition> Needs);

public static class Packs {
    public static SignalDefinition[] Load(params string[] paths) {
        var definitions=new List<SignalDefinition>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var path in paths) {
            var pack=JsonSerializer.Deserialize<SignalPack>(File.ReadAllText(path),
                new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
                ??throw new InvalidDataException("Missing pack");
            if(string.IsNullOrWhiteSpace(pack.Namespace)||pack.Namespace.Contains(':')
               ||pack.Needs.Count==0)throw new InvalidDataException("Invalid namespace/needs");
            foreach(var need in pack.Needs){
                string id=need.Id.Contains(':',StringComparison.Ordinal)
                    ?need.Id:pack.Namespace+":"+need.Id;
                if(!id.Contains(':')||need.Rate<=0||need.Rate>20||
                    need.Threshold is <100 or >1900||
                    need.Relief is <1 or >2000||
                    need.CoupledDelta is <0 or >200||
                    !seen.Add(id))throw new InvalidDataException("Invalid/duplicate signal "+id);
                definitions.Add(need with {Id=id});
            }
        }
        return definitions.ToArray();
    }
}
public sealed record Stimulus(int Tick,int Agent,int Signal,int Delta,string Root);
public static class Stimuli {
    public static Stimulus[] Generate(int seed,int agents,int ticks,int signalCount,double perAgent){
        if(perAgent<0||perAgent>100)throw new ArgumentOutOfRangeException(nameof(perAgent));
        int count=(int)Math.Round(perAgent*agents);
        var rng=new Random(seed^0x5EED03);
        var records=new Stimulus[count];
        for(int i=0;i<count;i++){
            int tick=rng.Next(1,ticks+1);
            int agent=rng.Next(agents);
            int signal=rng.Next(signalCount);
            int delta=rng.Next(150,601);
            records[i]=new Stimulus(tick,agent,signal,delta,$"observation:{i}");
        }
        return records.OrderBy(s=>s.Tick).ThenBy(s=>s.Agent).ThenBy(s=>s.Signal)
            .ThenBy(s=>s.Root,StringComparer.Ordinal).ToArray();
    }
}

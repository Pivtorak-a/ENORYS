using System.Text.Json;
using Enorys.Abstractions;
using Enorys.Core.Domain;
namespace Enorys.Domains.Survival;

/// <summary>
/// Approximate, user-configured background cognition deadlines. It does NOT
/// claim equal actions to running SurvivalBehavior every actor turn: its
/// threshold profile is an explicit Cognitive LOD budget and must be audited.
/// </summary>
public sealed class SurvivalBackgroundDeadline : IEnorysModule,IBackgroundDeadlinePolicy {
    private sealed record Rule(string Id,double Threshold);
    private sealed record Profile(string Namespace,string Version,int MaxRounds,List<Rule> Thresholds);
    private readonly (NeedDefinition Definition,double Threshold)[] thresholds;
    private readonly int budget;
    public string Id=>"survival:background-deadline";
    public string PolicyId=>Id;
    public SurvivalBackgroundDeadline(DomainRegistry domain,string jsonPath){
        var profile=JsonSerializer.Deserialize<Profile>(File.ReadAllText(jsonPath),
            new JsonSerializerOptions{PropertyNameCaseInsensitive=true})
            ??throw new InvalidDataException("Missing background deadline profile");
        if(string.IsNullOrWhiteSpace(profile.Namespace)||profile.MaxRounds is <1 or >100)
            throw new InvalidDataException("Invalid background profile");
        budget=profile.MaxRounds;
        var entries=new List<(NeedDefinition,double)>();
        var unique=new HashSet<string>(StringComparer.Ordinal);
        foreach(var rule in profile.Thresholds){
            string id=rule.Id.Contains(':')?rule.Id:profile.Namespace+":"+rule.Id;
            if(!domain.Needs.TryGetValue(id,out var definition)||!unique.Add(id)||
               !double.IsFinite(rule.Threshold)||rule.Threshold is <0 or >100)
                throw new InvalidDataException("Invalid/duplicate deadline rule "+id);
            entries.Add((definition,rule.Threshold));
        }
        if(entries.Count==0)throw new InvalidDataException("Empty deadline profile");
        thresholds=entries.ToArray();
    }
    public void Register(IModuleRegistry registry)=>registry.AddBackgroundDeadline(this);
    public int NextWakeRounds(AgentView actor,ObservationPacket observations,
        int roundsPerDay,int maxRounds){
        if(roundsPerDay<1||maxRounds<1)throw new ArgumentOutOfRangeException();
        int ceiling=Math.Min(budget,maxRounds);
        // If actor was just exposed to a message, reconsider on the next turn.
        if(observations.Messages.Count>0||actor.Health<60||actor.Injury>25||
           actor.Infection>20)return 1;
        // Called after world-side action commit: urgency uses current private needs.
        int min=ceiling;
        foreach(var (need,threshold) in thresholds){
            double level=actor.Needs.GetValueOrDefault(need.Id);
            if(level>=threshold)return 1;
            if(need.Daily<=0)continue;
            int days=(int)Math.Ceiling((threshold-level)/need.Daily);
            // Need increases at end-of-day, wake before crossing threshold.
            // Conservatively advance at most this many ACTOR rounds.
            int rounds=Math.Max(1,days*roundsPerDay-(roundsPerDay-1));
            min=Math.Min(min,rounds);
        }
        return Math.Clamp(min,1,ceiling);
    }
}

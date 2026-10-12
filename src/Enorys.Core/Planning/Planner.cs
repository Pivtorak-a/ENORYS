using Enorys.Abstractions;
using Enorys.Core.Domain;
namespace Enorys.Core.Planning;

public sealed class Plan {
    public string Goal="";
    public List<ActionProposal> Steps=new();
    public int Index;
    public int Replans;
    public bool Finished=>Index>=Steps.Count;
}
/// <summary>Bounded prerequisite expansion, reading only private inventory and observed source affordances.</summary>
public sealed class Planner(DomainRegistry domain) {
    public Plan? MakePlan(AgentView a,ObservationPacket obs,GoalDefinition goal,int maxSteps=8){
        var inventory=new Dictionary<string,int>(a.Inventory);
        var path=new List<ActionProposal>();
        bool Ensure(string item,int amount,int depth,HashSet<string> seen){
            if(inventory.GetValueOrDefault(item)>=amount)return true;
            if(depth>maxSteps||!seen.Add(item))return false;
            var missing=amount-inventory.GetValueOrDefault(item);
            var producer=domain.Recipes.Values.OrderBy(x=>x.Id,StringComparer.Ordinal)
                .FirstOrDefault(r=>r.Outputs.GetValueOrDefault(item)>0);
            if(producer!=null){
                int cycles=(int)Math.Ceiling((double)missing/producer.Outputs[item]);
                for(int i=0;i<cycles;i++){
                    foreach(var ingredient in producer.Inputs)
                        if(!Ensure(ingredient.Key,ingredient.Value,depth+1,new HashSet<string>(seen)))return false;
                    if(path.Count>=maxSteps)return false;
                    path.Add(new ActionProposal(a.Id,producer.Id));
                    foreach(var part in producer.Inputs)inventory[part.Key]-=part.Value;
                    foreach(var part in producer.Outputs)inventory[part.Key]=inventory.GetValueOrDefault(part.Key)+part.Value;
                }
            } else if(domain.Sources.TryGetValue(item,out var source)&&
                      obs.SourcesKnown.GetValueOrDefault(item)){
                if(path.Count+missing>maxSteps)return false;
                for(int i=0;i<missing;i++){path.Add(new ActionProposal(a.Id,source.ActionId));inventory[item]=inventory.GetValueOrDefault(item)+1;}
            }else return false;
            return true;
        }
        if(!Ensure(goal.ItemId,1,0,new HashSet<string>()))return null;
        if(path.Count>=maxSteps)return null;
        path.Add(new ActionProposal(a.Id,goal.UseAction));
        return new Plan{Goal=goal.Id,Steps=path};
    }
    public ActionProposal? Next(AgentView a,ObservationPacket obs,Plan? p){
        if(p==null||p.Finished)return null;
        var step=p.Steps[p.Index];
        if(domain.Recipes.TryGetValue(step.Kind,out var recipe))
            return recipe.Inputs.All(x=>a.Inventory.GetValueOrDefault(x.Key)>=x.Value)?step:null;
        if(domain.Sources.Values.FirstOrDefault(x=>x.ActionId==step.Kind) is { } sourcing)
            return obs.SourcesKnown.GetValueOrDefault(sourcing.ItemId)?step:null;
        var action=domain.Actions.GetValueOrDefault(step.Kind);
        if(action!=null)return a.Inventory.GetValueOrDefault(action.ItemId)>0?step:null;
        if(step.Kind=="survival:drink")return a.Inventory.GetValueOrDefault("survival:water")>0?step:null;
        if(step.Kind=="survival:eat")return a.Inventory.GetValueOrDefault("survival:food")>0?step:null;
        return step;
    }
    public int Unreserved(AgentView a,Plan? p,string item){
        int held=a.Inventory.GetValueOrDefault(item);
        if(p==null)return held;
        int demand=0;
        foreach(var step in p.Steps.Skip(p.Index)){
            if(domain.Recipes.TryGetValue(step.Kind,out var recipe))
                demand+=recipe.Inputs.GetValueOrDefault(item);
            if(domain.Actions.TryGetValue(step.Kind,out var action)&&action.ItemId==item)demand++;
            if(step.Kind=="survival:eat"&&item=="survival:food"||step.Kind=="survival:drink"&&item=="survival:water")demand++;
        }
        return Math.Max(0,held-Math.Min(held,demand));
    }
}

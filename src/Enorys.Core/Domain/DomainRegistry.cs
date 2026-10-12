using System.Text.Json;
using Enorys.Abstractions;
namespace Enorys.Core.Domain;

public sealed class DomainRegistry {
    private readonly Dictionary<string,NeedDefinition> _needs=new(StringComparer.Ordinal);
    private readonly Dictionary<string,ItemDefinition> _items=new(StringComparer.Ordinal);
    private readonly Dictionary<string,RecipeDefinition> _recipes=new(StringComparer.Ordinal);
    private readonly Dictionary<string,GoalDefinition> _goals=new(StringComparer.Ordinal);
    private readonly Dictionary<string,ActionDefinition> _actions=new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string,NeedDefinition> Needs=>_needs;
    public IReadOnlyDictionary<string,ItemDefinition> Items=>_items;
    public IReadOnlyDictionary<string,RecipeDefinition> Recipes=>_recipes;
    public IReadOnlyDictionary<string,GoalDefinition> Goals=>_goals;
    public IReadOnlyDictionary<string,ActionDefinition> Actions=>_actions;
    private static readonly JsonSerializerOptions Options = new(){PropertyNameCaseInsensitive=true};

    public static DomainRegistry Load(params string[] paths){
        var result=new DomainRegistry();
        foreach(var path in paths) {
            var pack=JsonSerializer.Deserialize<DomainPack>(File.ReadAllText(path),Options)??throw new InvalidDataException("Null pack");
            result.Add(pack);
        }
        result.ValidateReferences();return result;
    }
    public void Add(DomainPack pack){
        if(string.IsNullOrWhiteSpace(pack.Namespace)||pack.Namespace.Contains(':')||string.IsNullOrWhiteSpace(pack.Id))
            throw new InvalidDataException("Invalid namespace or pack ID");
        string Norm(string s) => s.Contains(':')?s:pack.Namespace+":"+s;
        void CheckId(string id){if(!id.Contains(':')||id.StartsWith(':')||id.EndsWith(':'))throw new InvalidDataException("Missing namespaced ID: "+id);}
        foreach(var x in pack.Needs){
            var y=x with {Id=Norm(x.Id)};CheckId(y.Id);
            if(!double.IsFinite(y.Daily)||y.Daily<0||y.Daily>100||!double.IsFinite(y.Weight)||y.Weight<0||y.Initial is <0 or >100||!double.IsFinite(y.Initial)||!_needs.TryAdd(y.Id,y))throw new InvalidDataException("Invalid/duplicate need "+y.Id);
        }
        foreach(var x in pack.Items){
            var y=x with {Id=Norm(x.Id)};CheckId(y.Id);
            if(string.IsNullOrWhiteSpace(y.Category)||!_items.TryAdd(y.Id,y))throw new InvalidDataException("Invalid/duplicate item "+y.Id);
        }
        foreach(var x in pack.Recipes){
            var y=x with {Id=Norm(x.Id),Inputs=x.Inputs.ToDictionary(k=>Norm(k.Key),v=>v.Value),Outputs:x.Outputs.ToDictionary(k=>Norm(k.Key),v=>v.Value)};CheckId(y.Id);
            if(y.Inputs.Count==0||y.Outputs.Count==0||y.Inputs.Values.Any(v=>v<=0)||y.Outputs.Values.Any(v=>v<=0)||!_recipes.TryAdd(y.Id,y))throw new InvalidDataException("Invalid/duplicate recipe "+y.Id);
        }
        foreach(var x in pack.Goals){
            var y=x with {Id=Norm(x.Id),NeedId=Norm(x.NeedId),ItemId=Norm(x.ItemId),UseAction=Norm(x.UseAction)};
            if(y.Threshold is <0 or >100||!double.IsFinite(y.Threshold)||!_goals.TryAdd(y.Id,y))throw new InvalidDataException("Invalid/duplicate goal "+y.Id);
        }
        foreach(var x in pack.Actions){
            var y=x with {Id=Norm(x.Id),ItemId=Norm(x.ItemId),NeedId=Norm(x.NeedId)};
            if(!double.IsFinite(y.NeedDelta)||!_actions.TryAdd(y.Id,y))throw new InvalidDataException("Invalid/duplicate action "+y.Id);
        }
    }
    private void ValidateReferences(){
        foreach(var x in _recipes.Values)
          foreach(var k in x.Inputs.Keys.Concat(x.Outputs.Keys))
            if(!_items.ContainsKey(k))throw new InvalidDataException("Unknown item "+k);
        foreach(var x in _actions.Values)
          if(!_items.ContainsKey(x.ItemId)||!_needs.ContainsKey(x.NeedId))throw new InvalidDataException("Unknown action reference "+x.Id);
        foreach(var x in _goals.Values)
          if(!_items.ContainsKey(x.ItemId)||!_needs.ContainsKey(x.NeedId)||(!_actions.ContainsKey(x.UseAction)&&x.UseAction!="survival:eat"&&x.UseAction!="survival:drink"))
            throw new InvalidDataException("Unknown goal reference "+x.Id);
    }
}
